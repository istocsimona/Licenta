using Licenta.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace Licenta.Controllers
{
    public class ItineraryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserManager<ApplicationUser> _userManager;

        public ItineraryController(
        ApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
        }

        [HttpPost]
        public async Task<IActionResult> ToggleVisitStatus(int itemId)
        {
            try
            {
                var item = await _context.ItineraryItems.FindAsync(itemId);

                if (item == null)
                    return Json(new { success = false, message = "Item not found" });

                if (item.VisitStatus == "Visited")
                    item.VisitStatus = "Planned";
                else
                    item.VisitStatus = "Visited";

                await _context.SaveChangesAsync();
                return Json(new { success = true, newStatus = item.VisitStatus });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateItinerary(int tripId)
        {
            try
            {
                var trip = await _context.Trips
                    .Include(t => t.Locations)
                    .Include(t => t.Reservations)
                    .Include(t => t.Accommodation)
                    .FirstOrDefaultAsync(t => t.TripId == tripId);

                if (trip == null) return Json(new { success = false, message = "Trip not found" });

                // 1. Get existing DayPlans
                var dayPlans = await _context.DayPlans
                    .Include(dp => dp.ItineraryItems)
                    .Where(dp => dp.TripId == tripId)
                    .ToListAsync();

                // SYNCHRONIZE DATES: Make sure we have a DayPlan for every day in the CURRENT trip range
                var validDates = new List<DateTime>();
                for (var d = trip.StartDate.Date; d <= trip.EndDate.Date; d = d.AddDays(1))
                {
                    validDates.Add(d);
                    if (!dayPlans.Any(dp => dp.Date == d))
                    {
                        var newDay = new DayPlan { TripId = tripId, Date = d, ItineraryItems = new List<ItineraryItem>() };
                        _context.DayPlans.Add(newDay);
                        dayPlans.Add(newDay);
                    }
                }

                // 2. Clean up old days and unvisited items
                DateTime today = DateTime.Now.Date;

                var itemsToRemove = new List<ItineraryItem>();
                var plansToRemove = new List<DayPlan>();

                foreach (var dp in dayPlans.ToList())
                {
                    var unvisited = dp.ItineraryItems.Where(i => i.VisitStatus != "Visited").ToList();
                    itemsToRemove.AddRange(unvisited);
                    foreach (var item in unvisited) dp.ItineraryItems.Remove(item);

                    // Visited items on FUTURE days also free their slot.
                    // We keep them in the DB (so history is preserved) but move them
                    // to today's DayPlan so they don't block tomorrow's schedule.
                    if (dp.Date.Date > today)
                    {
                        var visitedFuture = dp.ItineraryItems.Where(i => i.VisitStatus == "Visited").ToList();
                        if (visitedFuture.Any())
                        {
                            var todayPlan = dayPlans.FirstOrDefault(d => d.Date.Date == today);
                            foreach (var vis in visitedFuture)
                            {
                                dp.ItineraryItems.Remove(vis);
                                if (todayPlan != null)
                                {
                                    vis.DayPlanId = todayPlan.DayPlanId;
                                    todayPlan.ItineraryItems.Add(vis);
                                }
                                // If no todayPlan exists (trip starts in future), just leave them removed from future slot
                                // but keep in DB under their original plan — they won't be rescheduled since
                                // their LocationId will be in visitedLocationIds in step 3.
                            }
                        }
                    }

                    if (!validDates.Contains(dp.Date) && !dp.ItineraryItems.Any())
                    {
                        plansToRemove.Add(dp);
                        dayPlans.Remove(dp);
                    }
                }

                _context.ItineraryItems.RemoveRange(itemsToRemove);
                _context.DayPlans.RemoveRange(plansToRemove);
                await _context.SaveChangesAsync();

                // Ensure the days are strictly ordered chronologically for the routing loop
                dayPlans = dayPlans.OrderBy(dp => dp.Date).ToList();

                // 3. Figure out which locations are still waiting to be visited
                var tripLocations = trip.Locations ?? new List<Location>();
                var tripReservations = trip.Reservations ?? new List<Reservation>();


                var visitedLocationIds = dayPlans
                    .SelectMany(dp => dp.ItineraryItems)
                    .Where(i => i.VisitStatus == "Visited")
                    .Select(i => i.LocationId)
                    .ToList();

                var availableLocations = tripLocations
                    .Where(l => !visitedLocationIds.Contains(l.LocationId))
                    .Where(l => !tripReservations.Any(r => r.LocationId == l.LocationId))
                    .ToList();

                // 4. Rebuild the schedule, respecting the calendar

                foreach (var dayPlan in dayPlans)
                {
                    var date = dayPlan.Date;

                    // If this day is in the past, LOCK IT. Do not schedule missed items here.
                    if (date < today)
                    {
                        int idx = 1;
                        foreach (var vis in dayPlan.ItineraryItems.OrderBy(i => i.StartTime)) vis.OrderIndex = idx++;
                        continue;
                    }

                    // For Today and Future Days: Re-route everything!
                    TimeSpan currentTime = TimeSpan.FromHours(trip.StartExplorationHour);
                    TimeSpan endTimeLimit = TimeSpan.FromHours(trip.EndExplorationHour);

                    var todaysReservations = tripReservations.Where(r => r.StartDate.Date == date).OrderBy(r => r.StartDate).ToList();
                    var todaysVisited = dayPlan.ItineraryItems.OrderBy(i => i.StartTime).ToList();

                    int orderCounter = 1;
                    Location lastVisitedLocation = null;

                    if (trip.Accommodation != null)
                    {
                        lastVisitedLocation = new Location { LocationId = -1, Latitude = trip.Accommodation.Latitude, Longitude = trip.Accommodation.Longitude, Name = trip.Accommodation.Name };
                    }

                    TimeSpan previousTimeTracker = currentTime;

                    while (currentTime < endTimeLimit)
                    {
                        // Find the next fixed time (either a Reservation or an already Visited item from earlier today)
                        var nextRes = todaysReservations.FirstOrDefault(r => r.StartDate.TimeOfDay >= currentTime.Subtract(TimeSpan.FromMinutes(15)));
                        var nextVis = todaysVisited.FirstOrDefault(v => v.StartTime >= currentTime.Subtract(TimeSpan.FromMinutes(15)));

                        TimeSpan? nextConstraintTime = null;
                        bool isNextRes = false;

                        if (nextRes != null && nextVis != null)
                        {
                            if (nextRes.StartDate.TimeOfDay <= nextVis.StartTime) { nextConstraintTime = nextRes.StartDate.TimeOfDay; isNextRes = true; }
                            else { nextConstraintTime = nextVis.StartTime; isNextRes = false; }
                        }
                        else if (nextRes != null) { nextConstraintTime = nextRes.StartDate.TimeOfDay; isNextRes = true; }
                        else if (nextVis != null) { nextConstraintTime = nextVis.StartTime; isNextRes = false; }

                        // If we bumped into a Fixed item, process it
                        if (nextConstraintTime.HasValue && currentTime >= nextConstraintTime.Value.Subtract(TimeSpan.FromMinutes(15)))
                        {
                            if (isNextRes)
                            {
                                if (nextRes.LocationId.HasValue)
                                {
                                    dayPlan.ItineraryItems.Add(new ItineraryItem { LocationId = nextRes.LocationId.Value, StartTime = nextRes.StartDate.TimeOfDay, EndTime = nextRes.EndDate.TimeOfDay, VisitStatus = "Reservation", OrderIndex = orderCounter++ });
                                    lastVisitedLocation = tripLocations.FirstOrDefault(l => l.LocationId == nextRes.LocationId.Value);
                                }
                                currentTime = (nextRes.EndDate.TimeOfDay > currentTime) ? nextRes.EndDate.TimeOfDay : currentTime.Add(TimeSpan.FromMinutes(15));
                                todaysReservations.Remove(nextRes);
                            }
                            else
                            {
                                nextVis.OrderIndex = orderCounter++;
                                lastVisitedLocation = tripLocations.FirstOrDefault(l => l.LocationId == nextVis.LocationId);
                                currentTime = (nextVis.EndTime > currentTime) ? nextVis.EndTime : currentTime.Add(TimeSpan.FromMinutes(15));
                                todaysVisited.Remove(nextVis);
                            }
                            continue;
                        }

                        // if we have free time Mapbox finds the best unvisited location
                        var routingResult = await FindBestNextLocationAsync(lastVisitedLocation, availableLocations, currentTime, nextConstraintTime, endTimeLimit);
                        Location bestLocation = routingResult.Location;
                        int travelTime = routingResult.TravelTime;

                        if (bestLocation != null)
                        {
                            if (lastVisitedLocation != null) currentTime = currentTime.Add(TimeSpan.FromMinutes(travelTime));

                            dayPlan.ItineraryItems.Add(new ItineraryItem
                            {
                                LocationId = bestLocation.LocationId,
                                StartTime = currentTime,
                                EndTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration)),
                                VisitStatus = "Planned",
                                OrderIndex = orderCounter++
                            });

                            lastVisitedLocation = bestLocation;
                            currentTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration));
                            availableLocations.Remove(bestLocation);
                        }
                        else
                        {
                            if (nextConstraintTime.HasValue)
                            {
                                var targetTime = nextConstraintTime.Value.Subtract(TimeSpan.FromMinutes(15));
                                currentTime = (targetTime > currentTime) ? targetTime : currentTime.Add(TimeSpan.FromMinutes(15));
                            }
                            else break;
                        }

                        if (currentTime <= previousTimeTracker) currentTime = currentTime.Add(TimeSpan.FromMinutes(15));
                        previousTimeTracker = currentTime;
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                string errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = errorMessage });
            }
        }

        // ------------------------------------------------------------------
        // HELPER LOGIC
        // ------------------------------------------------------------------
        private bool IsLocationOpen(Location l, TimeSpan current)
        {
            if (string.IsNullOrEmpty(l.OpeningHour) || string.IsNullOrEmpty(l.ClosingHour)) return true;

            if (TimeSpan.TryParse(l.OpeningHour, out TimeSpan openTime) &&
                TimeSpan.TryParse(l.ClosingHour, out TimeSpan closeTime))
            {
                TimeSpan actualCloseTime = closeTime;
                if (closeTime <= openTime) actualCloseTime = closeTime.Add(TimeSpan.FromHours(24));

                TimeSpan finishTime = current.Add(TimeSpan.FromMinutes(l.AvgDuration));
                return current >= openTime && finishTime <= actualCloseTime;
            }
            return true;
        }

        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        // ------------------------------------------------------------------
        // NAME MATCHING (so "Pantheon" doesn't get matched to a storage unit,
        // and "Vatican Museums" can still match Mapbox's "Musei Vaticani")
        // ------------------------------------------------------------------

        /// <summary>
        /// Strips accents/diacritics and lowercases, so "Musei Vaticani" and
        /// "MUSEI VATICANI" compare the same, and accented local names compare
        /// fairly against their AI/English equivalents.
        /// </summary>
        private string NormalizeForMatch(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var formD = input.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in formD)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private static readonly HashSet<string> CommonWords = new HashSet<string>
        {
            "the", "of", "and", "de", "del", "della", "di", "la", "le", "il", "el", "los", "las",
            "museum", "museo", "musee", "musei", "church", "chiesa", "basilica", "park", "parco",
            "garden", "gardens", "square", "piazza", "plaza", "palace", "palazzo", "castle",
            "castello", "tower", "torre", "national", "royal", "old", "ancient"
        };

        // ------------------------------------------------------------------
        // NON-ATTRACTION FILTERING (lodging, vacation rentals, wellness venues,
        // and other commercial/private listings that Mapbox indexes but that
        // are not genuine sightseeing destinations)
        // ------------------------------------------------------------------

        /// <summary>
        /// Words commonly found in vacation-rental / lodging / wellness listing titles.
        /// Listing titles are written by hosts as marketing copy ("Beautiful penthouse
        /// with terrace...", "Rooftop Jacuzzi with stunning view!") so this list is kept
        /// broad and is reviewed alongside NonAttractionCategories, which catches listings
        /// whose titles dodge every keyword here.
        /// </summary>
        private static readonly HashSet<string> NonAttractionKeywords = new HashSet<string>
        {
            "hotel", "apartment", "apartments", "penthouse", "studio", "bedroom", "bnb", "b&b",
            "guesthouse", "guest house", "hostel", "resort", "loft", "flat", "condo",
            "parking", "yespark", "suite", "room", "storage", "self storage",
            "jacuzzi", "sauna", "spa", "hot tub", "rooftop", "terrace", "balcony",
            "rental", "vacation rental", "airbnb", "booking.com", "lodging",
            "with view", "stunning view", "private pool", "swimming pool"
        };

        /// <summary>
        /// Substrings of Mapbox's poi_category values that indicate a commercial/private
        /// venue rather than a genuine public attraction. Checking the category (assigned
        /// by Mapbox's own data) catches listings even when the title itself is generic
        /// enough to dodge NonAttractionKeywords.
        /// </summary>
        private static readonly HashSet<string> NonAttractionCategories = new HashSet<string>
        {
            "lodging", "hotel", "motel", "hostel", "vacation rental", "bed and breakfast",
            "resort", "spa", "massage", "sauna", "parking", "self storage", "storage",
            "real estate", "property management", "rental"
        };

        /// <summary>
        /// Scores how well a geocoded result's name matches what was actually asked for.
        /// Returns 0 (no relation) to 1 (essentially the same place). Designed to tolerate
        /// local-language aliases (e.g. "Vatican Museums" vs "Musei Vaticani" share the
        /// "vatican/vaticani" token) while still rejecting unrelated businesses that merely
        /// happen to sit inside the right city ("Pantheon" vs "ABC Self Storage").
        /// </summary>
        private double NameSimilarityScore(string requestedName, string resultName)
        {
            string a = NormalizeForMatch(requestedName);
            string b = NormalizeForMatch(resultName);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

            if (a == b) return 1.0;
            if (a.Contains(b) || b.Contains(a)) return 0.85;

            var tokensA = a.Split(new[] { ' ', '-', ',', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(t => t.Length > 2).ToList();
            var tokensB = b.Split(new[] { ' ', '-', ',', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(t => t.Length > 2).ToList();
            if (tokensA.Count == 0 || tokensB.Count == 0) return 0;

            // Significant tokens (drop generic words like "museum"/"the"/"palace") carry
            // more weight, since two places can both be "museums" without being the same
            // museum, but sharing a distinctive word like "vatican" is a strong signal.
            var significantA = tokensA.Where(t => !CommonWords.Contains(t)).ToList();
            var significantB = tokensB.Where(t => !CommonWords.Contains(t)).ToList();

            int sharedSignificant = significantA.Count(ta => significantB.Any(tb => TokensMatch(ta, tb)));
            int sharedAny = tokensA.Count(ta => tokensB.Any(tb => TokensMatch(ta, tb)));

            int significantDenominator = Math.Max(1, Math.Min(significantA.Count, significantB.Count));
            int anyDenominator = Math.Max(1, Math.Min(tokensA.Count, tokensB.Count));

            double significantRatio = significantA.Count > 0 && significantB.Count > 0
                ? (double)sharedSignificant / significantDenominator
                : 0;
            double anyRatio = (double)sharedAny / anyDenominator;

            // Weight distinctive-word overlap heavily; generic-word overlap alone
            // (e.g. both contain "museum") shouldn't be enough to pass.
            return Math.Max(significantRatio, anyRatio * 0.5);
        }

        /// <summary>
        /// Compares two normalized tokens allowing for shared word stems, so
        /// "vatican" matches "vaticani", "gallery" loosely matches "galleria", etc.
        /// </summary>
        private bool TokensMatch(string tokenA, string tokenB)
        {
            if (tokenA == tokenB) return true;
            if (tokenA.Length >= 5 && tokenB.Length >= 5)
            {
                int stemLen = Math.Min(tokenA.Length, tokenB.Length) - 1;
                if (stemLen >= 4 && tokenA.Substring(0, stemLen) == tokenB.Substring(0, stemLen)) return true;
            }
            return false;
        }

        /// <summary>
        /// Best-effort hint for the local language to additionally request from Mapbox,
        /// so the geocoder is more likely to surface (or match against) the name the
        /// place is actually known by locally, e.g. "musei vaticani" for Vatican Museums.
        /// </summary>
        private string GetLocalLanguageHint(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return "en";

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["italy"] = "it",
                ["italia"] = "it",
                ["france"] = "fr",
                ["spain"] = "es",
                ["españa"] = "es",
                ["germany"] = "de",
                ["deutschland"] = "de",
                ["romania"] = "ro",
                ["portugal"] = "pt",
                ["greece"] = "el",
                ["netherlands"] = "nl",
                ["austria"] = "de",
                ["switzerland"] = "de",
                ["poland"] = "pl",
                ["czech republic"] = "cs",
                ["czechia"] = "cs",
                ["turkey"] = "tr",
                ["türkiye"] = "tr",
                ["japan"] = "ja",
                ["china"] = "zh",
                ["south korea"] = "ko",
                ["russia"] = "ru",
                ["egypt"] = "ar",
                ["morocco"] = "ar",
            };

            return map.TryGetValue(country.Trim(), out var code) ? code : "en";
        }

        private bool FitsBeforeNextEvent(Location l, TimeSpan current, TimeSpan? nextConstraintTime, TimeSpan endLimit)
        {
            var finishTime = current.Add(TimeSpan.FromMinutes(l.AvgDuration));

            if (nextConstraintTime.HasValue)
            {
                var arrivalDeadLine = nextConstraintTime.Value.Subtract(TimeSpan.FromMinutes(15));
                if (finishTime > arrivalDeadLine) return false;
            }

            return finishTime <= endLimit;
        }

        // ------------------------------------------------------------------
        // MAPBOX ROUTING LOGIC
        // ------------------------------------------------------------------
        private async Task<(Location Location, int TravelTime)> FindBestNextLocationAsync(
            Location lastLocation,
            List<Location> destinations,
            TimeSpan currentTime,
            TimeSpan? nextConstraintTime,
            TimeSpan endTimeLimit)
        {
            if (lastLocation == null)
            {
                var firstLoc = destinations.FirstOrDefault(l => IsLocationOpen(l, currentTime) && FitsBeforeNextEvent(l, currentTime, nextConstraintTime, endTimeLimit));
                return (firstLoc, 0);
            }

            var candidates = destinations.Count > 24
                ? destinations.OrderBy(d => GetDistance(lastLocation.Latitude, lastLocation.Longitude, d.Latitude, d.Longitude)).Take(24).ToList()
                : destinations;

            var mapboxToken = _configuration["Mapbox:PublicKey"];
            List<double> walkingDurations = null;
            List<double> drivingDurations = null;

            if (!string.IsNullOrEmpty(mapboxToken))
            {
                var coordsString = $"{lastLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lastLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};" +
                                   string.Join(";", candidates.Select(c => $"{c.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{c.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

                var client = _httpClientFactory.CreateClient();

                try
                {
                    var wRes = await client.GetAsync($"https://api.mapbox.com/directions-matrix/v1/mapbox/walking/{coordsString}?sources=0&annotations=duration&access_token={mapboxToken}");
                    if (wRes.IsSuccessStatusCode)
                    {
                        var data = JsonSerializer.Deserialize<MapboxMatrixResponse>(await wRes.Content.ReadAsStringAsync());
                        if (data?.code == "Ok" && data.durations?.Count > 0) walkingDurations = data.durations[0];
                    }
                }
                catch { }

                try
                {
                    var dRes = await client.GetAsync($"https://api.mapbox.com/directions-matrix/v1/mapbox/driving/{coordsString}?sources=0&annotations=duration&access_token={mapboxToken}");
                    if (dRes.IsSuccessStatusCode)
                    {
                        var data = JsonSerializer.Deserialize<MapboxMatrixResponse>(await dRes.Content.ReadAsStringAsync());
                        if (data?.code == "Ok" && data.durations?.Count > 0) drivingDurations = data.durations[0];
                    }
                }
                catch { }
            }

            Location bestLocation = null;
            double bestTravelTimeSeconds = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                int matrixIndex = i + 1;

                double walkSecs = walkingDurations != null && matrixIndex < walkingDurations.Count ? walkingDurations[matrixIndex] : double.MaxValue;
                double driveSecs = drivingDurations != null && matrixIndex < drivingDurations.Count ? drivingDurations[matrixIndex] : double.MaxValue;

                double minSecs = Math.Min(walkSecs, driveSecs);

                if (minSecs == double.MaxValue)
                {
                    minSecs = Math.Max(5 * 60, (GetDistance(lastLocation.Latitude, lastLocation.Longitude, candidate.Latitude, candidate.Longitude) / 166.0) * 60);
                }

                int travelMinutes = (int)Math.Ceiling(minSecs / 60.0);
                TimeSpan projectedArrival = currentTime.Add(TimeSpan.FromMinutes(travelMinutes));

                if (IsLocationOpen(candidate, projectedArrival) && FitsBeforeNextEvent(candidate, projectedArrival, nextConstraintTime, endTimeLimit))
                {
                    if (minSecs < bestTravelTimeSeconds)
                    {
                        bestTravelTimeSeconds = minSecs;
                        bestLocation = candidate;
                    }
                }
            }

            if (bestLocation == null) return (null, 0);

            return (bestLocation, (int)Math.Ceiling(bestTravelTimeSeconds / 60.0));
        }

        public class MapboxMatrixResponse
        {
            public string code { get; set; }
            public List<List<double>> durations { get; set; }
            public List<List<double>> distances { get; set; }
        }

        // ==========================================================
        // AI GENERATION 
        // ==========================================================
        [HttpPost]
        public async Task<IActionResult> GenerateWithAI(int tripId)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                var trip = await _context.Trips
                    .Include(t => t.Locations)
                    .FirstOrDefaultAsync(t => t.TripId == tripId);

                if (trip == null) return Json(new { success = false, message = "Trip not found." });

                var topTags = await _context.UserTagPreferences
                    .Include(p => p.Tag)
                    .Where(p => p.UserId == userId && p.RankOrder <= 3)
                    .OrderBy(p => p.RankOrder)
                    .Select(p => p.Tag)
                    .ToListAsync();

                if (!topTags.Any())
                {
                    return Json(new
                    {
                        success = false,
                        needsPreferences = true,
                        message = "You don't have your preferences selected."
                    });
                }

                var mapboxToken = _configuration["Mapbox:PublicKey"];
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TravelPlannerLicenta");

                string groqApiKey = "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";
                Uri groqEndpoint = new Uri("https://api.groq.com/openai/v1/chat/completions");

                var addedLocations = new List<Location>();
                List<AiLocationSuggestion> recommendedPlaces = new List<AiLocationSuggestion>();

                double cityLng = 0;
                double cityLat = 0;
                string cityCenterUrl = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{Uri.EscapeDataString($"{trip.City}, {trip.Country}")}.json?types=place,locality,neighborhood&access_token={mapboxToken}";

                try
                {
                    var cityRes = await client.GetAsync(new Uri(cityCenterUrl));
                    if (cityRes.IsSuccessStatusCode)
                    {
                        using var cityDoc = JsonDocument.Parse(await cityRes.Content.ReadAsStringAsync());
                        var features = cityDoc.RootElement.GetProperty("features");
                        if (features.GetArrayLength() > 0)
                        {
                            var center = features[0].GetProperty("center");
                            cityLng = center[0].GetDouble();
                            cityLat = center[1].GetDouble();
                        }
                    }
                }
                catch { }

                string tagsListString = string.Join(", ", topTags.Select(t => t.Name));

                // ── Scale the number of requested places to the actual trip length ──
                // Roughly one stop per ~2.5h of exploration time per day, with sensible
                // floor/ceiling so very short or very long trips still get a reasonable,
                // non-fixed number of suggestions.
                int tripDays = Math.Max(1, (trip.EndDate.Date - trip.StartDate.Date).Days + 1);
                double exploreHoursPerDay = Math.Max(1, trip.EndExplorationHour - trip.StartExplorationHour);
                int placesPerDay = (int)Math.Ceiling(exploreHoursPerDay / 2.5); // ~2.5h per stop incl. travel
                int targetPlaceCount = tripDays * placesPerDay;
                targetPlaceCount = Math.Clamp(targetPlaceCount, 6, 40);

                var aiRequestPayload = new
                {
                    model = "llama-3.3-70b-versatile",
                    temperature = 0.1,
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = "You are a travel API. Output ONLY raw JSON. Your output must exactly match this schema: {\"attractions\": [{\"name\": \"Exact Name\", \"tag\": \"Category\"}]}" },
                        new { role = "user", content = $"Give me up to {targetPlaceCount} real, visitable PUBLIC places of interest in or very near {trip.City}, {trip.Country}. This is for a {tripDays}-day trip, so suggest enough variety to fill every day without repeats. This may be a small village - if so, include nearby natural spots, viewpoints, churches, monuments, local parks, or any point of interest within 15km. Prioritize these categories: {tagsListString}. Each suggestion MUST be a genuine, publicly-accessible landmark, museum, monument, park, or natural site with its own official name - the kind of place that would appear in a guidebook. Do NOT include neighborhoods, hotels, restaurants, vacation rentals, Airbnb-style listings, private apartments, rooftop bars or terraces, spas, jacuzzis, or any commercial/private venue named with marketing language (e.g. \"Beautiful penthouse with terrace\", \"Rooftop Jacuzzi with view\"). Use their official real-world names (and include the well-known local-language name in parentheses if it differs, e.g. \"Vatican Museums (Musei Vaticani)\"). If fewer than {targetPlaceCount} genuinely exist, return only what truly exists." }
                    }
                };

                try
                {
                    var aiRequest = new HttpRequestMessage(HttpMethod.Post, groqEndpoint);
                    aiRequest.Headers.Add("Authorization", $"Bearer {groqApiKey}");
                    aiRequest.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(aiRequestPayload), System.Text.Encoding.UTF8, "application/json");

                    var aiResponse = await client.SendAsync(aiRequest);
                    if (aiResponse.IsSuccessStatusCode)
                    {
                        var aiJsonString = await aiResponse.Content.ReadAsStringAsync();

                        // ── DEBUG: write raw Groq response to file (overwrites each time) ──
                        var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "groq_debug.txt");
                        using (var debugFile = new StreamWriter(debugPath, append: false))
                        {
                            debugFile.WriteLine($"=== GROQ DEBUG [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
                            debugFile.WriteLine();
                            debugFile.WriteLine("--- RAW RESPONSE ---");
                            debugFile.WriteLine(aiJsonString);
                        }
                        // ─────────────────────────────────────────────────────────────────

                        using var aiDoc = JsonDocument.Parse(aiJsonString);

                        string rawContentJson = aiDoc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString();

                        int startIndex = rawContentJson.IndexOf('{');
                        int endIndex = rawContentJson.LastIndexOf('}');

                        if (startIndex >= 0 && endIndex >= 0)
                        {
                            rawContentJson = rawContentJson.Substring(startIndex, endIndex - startIndex + 1);
                        }
                        else
                        {
                            return Json(new { success = false, message = "AI Error: The AI did not return a valid JSON object." });
                        }

                        // ── DEBUG: append parsed content to the same file ──────────────
                        using (var debugFile = new StreamWriter(debugPath, append: true))
                        {
                            debugFile.WriteLine();
                            debugFile.WriteLine("--- PARSED ATTRACTIONS JSON ---");
                            debugFile.WriteLine(rawContentJson);
                        }
                        // ──────────────────────────────────────────────────────────────

                        using var contentDoc = JsonDocument.Parse(rawContentJson);
                        var attractionsArray = contentDoc.RootElement.GetProperty("attractions");

                        recommendedPlaces = System.Text.Json.JsonSerializer.Deserialize<List<AiLocationSuggestion>>(attractionsArray.GetRawText());
                    }
                    else
                    {
                        string errorMsg = await aiResponse.Content.ReadAsStringAsync();

                        // ── DEBUG: write error response to file (overwrites each time) ──
                        var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "groq_debug.txt");
                        using (var debugFile = new StreamWriter(debugPath, append: false))
                        {
                            debugFile.WriteLine($"=== GROQ DEBUG [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
                            debugFile.WriteLine();
                            debugFile.WriteLine($"--- ERROR (HTTP {(int)aiResponse.StatusCode}) ---");
                            debugFile.WriteLine(errorMsg);
                        }
                        // ─────────────────────────────────────────────────────────────

                        return Json(new { success = false, message = $"AI Engine Error: {errorMsg}" });
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Failed to parse AI response. " + ex.Message });
                }

                if (recommendedPlaces == null || !recommendedPlaces.Any())
                {
                    return Json(new { success = false, message = $"The AI returned an empty list for {trip.City}." });
                }

                foreach (var item in recommendedPlaces)
                {
                    if (addedLocations.Count >= targetPlaceCount) break;

                    // Try the AI's name as given first; if nothing scores well enough,
                    // retry without the city suffix (helps when Mapbox's POI name is in
                    // the local language and the city name confuses the match, e.g.
                    // "Vatican Museums, Rome" vs the POI being named "Musei Vaticani").
                    var queryAttempts = new List<string>
                    {
                        $"{item.name}, {trip.City}",
                        item.name
                    };

                    string candidateBestName = null;
                    double candidateBestScore = 0;
                    string candidateBestAddress = "";
                    double candidateBestLat = 0, candidateBestLng = 0;

                    foreach (var geocodeQuery in queryAttempts)
                    {
                        string url = $"https://api.mapbox.com/search/searchbox/v1/forward?q={Uri.EscapeDataString(geocodeQuery)}&access_token={mapboxToken}&limit=5&types=poi,place,locality,neighborhood&language=en,{GetLocalLanguageHint(trip.Country)}";

                        if (cityLat != 0 && cityLng != 0)
                        {
                            string strLng = cityLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            string strLat = cityLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            url += $"&proximity={strLng},{strLat}";
                        }

                        try
                        {
                            var response = await client.GetAsync(new Uri(url));
                            if (!response.IsSuccessStatusCode) continue;

                            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            var features = doc.RootElement.GetProperty("features");

                            foreach (var feature in features.EnumerateArray())
                            {
                                var props = feature.GetProperty("properties");
                                string validatedName = props.TryGetProperty("name", out var n) ? n.GetString() : item.name;
                                if (string.IsNullOrWhiteSpace(validatedName)) continue;

                                string lowerName = validatedName.ToLowerInvariant();
                                if (NonAttractionKeywords.Any(kw => lowerName.Contains(kw)))
                                {
                                    continue;
                                }

                                string fullAddress = props.TryGetProperty("full_address", out var addr) ? (addr.GetString() ?? "") : "";
                                string placeFormatted = props.TryGetProperty("place_formatted", out var pf) ? (pf.GetString() ?? "") : "";
                                string featureType = props.TryGetProperty("feature_type", out var ft) ? (ft.GetString() ?? "") : "";

                                // poi_category is an array of category strings Mapbox assigns
                                // (e.g. "lodging", "vacation rental", "spa", "historic site").
                                // This catches commercial/private venues even when the listing's
                                // NAME itself dodges the keyword blocklist (creative Airbnb titles,
                                // marketing copy, etc.) since the category is assigned by Mapbox's
                                // own data, not by whoever wrote the listing title.
                                var poiCategories = new List<string>();
                                if (props.TryGetProperty("poi_category", out var pc) && pc.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var catEl in pc.EnumerateArray())
                                    {
                                        var catStr = catEl.GetString();
                                        if (!string.IsNullOrEmpty(catStr)) poiCategories.Add(catStr.ToLowerInvariant());
                                    }
                                }
                                bool isNonAttractionCategory = poiCategories.Any(cat => NonAttractionCategories.Any(bad => cat.Contains(bad)));
                                if (isNonAttractionCategory) continue;

                                bool matchesCountry = fullAddress.Contains(trip.Country, StringComparison.OrdinalIgnoreCase) || placeFormatted.Contains(trip.Country, StringComparison.OrdinalIgnoreCase);
                                bool matchesCity = fullAddress.Contains(trip.City, StringComparison.OrdinalIgnoreCase) || placeFormatted.Contains(trip.City, StringComparison.OrdinalIgnoreCase);

                                double resultLat = 0, resultLng = 0;
                                bool matchesProximity = false;
                                if (feature.TryGetProperty("geometry", out var geom))
                                {
                                    var resultCoords = geom.GetProperty("coordinates");
                                    resultLng = resultCoords[0].GetDouble();
                                    resultLat = resultCoords[1].GetDouble();

                                    if (!matchesCity && cityLat != 0 && cityLng != 0)
                                    {
                                        double distanceMeters = GetDistance(cityLat, cityLng, resultLat, resultLng);
                                        matchesProximity = distanceMeters <= 30000; // 30km radius
                                    }
                                }

                                if (!matchesCountry || (!matchesCity && !matchesProximity)) continue;

                                // ── This is the key check that was missing: does the result's
                                // NAME actually resemble what the AI asked for? Without this,
                                // any random POI/business "in the right city" gets accepted as
                                // a match (e.g. a self-storage unit matching "Pantheon").
                                double nameScore = NameSimilarityScore(item.name, validatedName);

                                // Small bonus for being a real point of interest rather than a
                                // generic place/locality/neighborhood placeholder result.
                                double typeBonus = featureType.Equals("poi", StringComparison.OrdinalIgnoreCase) ? 0.1 : 0;
                                double totalScore = nameScore + typeBonus;

                                if (totalScore > candidateBestScore)
                                {
                                    bool isDuplicate = trip.Locations.Any(l => l.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase)) ||
                                                       addedLocations.Any(l => l.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase));
                                    if (isDuplicate) continue;

                                    candidateBestScore = totalScore;
                                    candidateBestName = validatedName;
                                    candidateBestAddress = !string.IsNullOrEmpty(fullAddress) ? fullAddress : placeFormatted;
                                    candidateBestLat = resultLat;
                                    candidateBestLng = resultLng;
                                }
                            }
                        }
                        catch { }

                        // Good enough match found on this attempt — no need to retry with
                        // the alternate query form.
                        if (candidateBestScore >= 0.6) break;
                    }

                    // Reject low-confidence matches instead of silently keeping a wrong
                    // place. A genuine match (even via local-language alias) should score
                    // well above this; "happened to be in the same city" alone should not.
                    if (candidateBestName == null || candidateBestScore < 0.35) continue;

                    var targetTag = topTags.FirstOrDefault(t => t.Name.Equals(item.tag, StringComparison.OrdinalIgnoreCase)) ?? topTags.First();

                    var newLoc = new Location
                    {
                        TripId = tripId,
                        Name = candidateBestName,
                        City = trip.City,
                        Address = candidateBestAddress,
                        Latitude = candidateBestLat,
                        Longitude = candidateBestLng,
                        AvgDuration = 90,
                        IsIndoor = true
                    };

                    _context.Locations.Add(newLoc);
                    addedLocations.Add(newLoc);
                    _context.LocationTags.Add(new LocationTag { Location = newLoc, TagId = targetTag.TagId });
                }

                if (!addedLocations.Any())
                {
                    return Json(new { success = false, message = "AI gave suggestions, but Mapbox couldn't find their locations inside your destination city boundaries." });
                }

                await _context.SaveChangesAsync();
                return await GenerateItinerary(tripId);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class AiLocationSuggestion
        {
            public string name { get; set; }
            public string tag { get; set; }
        }
    }
}