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
        public async Task<IActionResult> GenerateItinerary(int tripId)
        {
            try
            {
                var trip = await _context.Trips
                    .Include(t => t.Locations)
                    .Include(t => t.Reservations)
                    .Include(t => t.Accommodation) // 🌟 NEW: Include the Accommodation in the database query
                    .FirstOrDefaultAsync(t => t.TripId == tripId);

                if (trip == null) return Json(new { success = false, message = "Trip not found" });

                var oldDayPlans = await _context.DayPlans
                    .Include(dp => dp.ItineraryItems)
                    .Where(dp => dp.TripId == tripId)
                    .ToListAsync();

                foreach (var plan in oldDayPlans)
                {
                    _context.ItineraryItems.RemoveRange(plan.ItineraryItems);
                }
                _context.DayPlans.RemoveRange(oldDayPlans);
                await _context.SaveChangesAsync();

                var tripLocations = trip.Locations ?? new List<Location>();
                var tripReservations = trip.Reservations ?? new List<Reservation>();

                var availableLocations = tripLocations
                    .Where(l => !tripReservations.Any(r => r.LocationId == l.LocationId))
                    .ToList();

                // Loop through each day of the trip
                for (var date = trip.StartDate.Date; date <= trip.EndDate.Date; date = date.AddDays(1))
                {
                    var dayPlan = new DayPlan { TripId = tripId, Date = date, ItineraryItems = new List<ItineraryItem>() };

                    TimeSpan currentTime = TimeSpan.FromHours(trip.StartExplorationHour);
                    TimeSpan endTimeLimit = TimeSpan.FromHours(trip.EndExplorationHour);

                    var todaysReservations = tripReservations
                        .Where(r => r.StartDate.Date == date)
                        .OrderBy(r => r.StartDate).ToList();

                    int orderCounter = 1;
                    Location lastVisitedLocation = null;

                    // 🌟 NEW: If the trip has an accommodation, set it as the starting point for every morning!
                    if (trip.Accommodation != null)
                    {
                        lastVisitedLocation = new Location
                        {
                            LocationId = -1, // Dummy ID so it doesn't conflict with DB
                            Latitude = trip.Accommodation.Latitude,
                            Longitude = trip.Accommodation.Longitude,
                            Name = trip.Accommodation.Name
                        };
                    }

                    // Failsafe: Prevent infinite loops by ensuring time always moves forward
                    TimeSpan previousTimeTracker = currentTime;

                    while (currentTime < endTimeLimit)
                    {
                        var nextRes = todaysReservations.FirstOrDefault();

                        // 1. Process Reservations First
                        if (nextRes != null && currentTime >= nextRes.StartDate.TimeOfDay.Subtract(TimeSpan.FromMinutes(15)))
                        {
                            if (nextRes.LocationId.HasValue)
                            {
                                dayPlan.ItineraryItems.Add(new ItineraryItem
                                {
                                    LocationId = nextRes.LocationId.Value,
                                    StartTime = nextRes.StartDate.TimeOfDay, // Exact ticket time
                                    EndTime = nextRes.EndDate.TimeOfDay,
                                    VisitStatus = "Reservation",
                                    OrderIndex = orderCounter++
                                });

                                lastVisitedLocation = tripLocations.FirstOrDefault(l => l.LocationId == nextRes.LocationId.Value);
                            }

                            // Force time to move forward to prevent server crashes
                            if (nextRes.EndDate.TimeOfDay > currentTime)
                            {
                                currentTime = nextRes.EndDate.TimeOfDay;
                            }
                            else
                            {
                                currentTime = currentTime.Add(TimeSpan.FromMinutes(15));
                            }

                            todaysReservations.Remove(nextRes);
                            continue;
                        }

                        // 2 & 3. Find the best location using true Walking/Driving travel time
                        var routingResult = await FindBestNextLocationAsync(lastVisitedLocation, availableLocations, currentTime, nextRes, endTimeLimit);
                        Location bestLocation = routingResult.Location;
                        int travelTime = routingResult.TravelTime;

                        if (bestLocation != null)
                        {
                            // If we had to travel here, advance the clock by the exact travel time BEFORE starting the activity
                            // 🌟 Because the hotel is the 'lastVisitedLocation' in the morning, this will now add the travel time from the hotel to the first location!
                            if (lastVisitedLocation != null)
                            {
                                currentTime = currentTime.Add(TimeSpan.FromMinutes(travelTime));
                            }

                            dayPlan.ItineraryItems.Add(new ItineraryItem
                            {
                                LocationId = bestLocation.LocationId,
                                StartTime = currentTime,
                                EndTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration)),
                                VisitStatus = "Planned",
                                OrderIndex = orderCounter++
                            });

                            lastVisitedLocation = bestLocation;
                            // Advance the clock by the duration of the visit
                            currentTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration));
                            availableLocations.Remove(bestLocation);
                        }
                        else
                        {
                            // If no locations fit, fast-forward directly to the next reservation
                            if (nextRes != null)
                            {
                                var targetTime = nextRes.StartDate.TimeOfDay.Subtract(TimeSpan.FromMinutes(15));

                                if (targetTime > currentTime)
                                {
                                    currentTime = targetTime;
                                }
                                else
                                {
                                    currentTime = currentTime.Add(TimeSpan.FromMinutes(15)); // Failsafe
                                }
                            }
                            else
                            {
                                break; // End day timeline construction
                            }
                        }

                        // ABSOLUTE FAILSAFE: If time gets stuck in a loop, force it forward to prevent 500 Server Error
                        if (currentTime <= previousTimeTracker)
                        {
                            currentTime = currentTime.Add(TimeSpan.FromMinutes(15));
                        }
                        previousTimeTracker = currentTime;
                    }

                    _context.DayPlans.Add(dayPlan);
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
        // FIX: MILITARY TIME WRAP-AROUND LOGIC
        // ------------------------------------------------------------------
        private bool IsLocationOpen(Location l, TimeSpan current)
        {
            if (string.IsNullOrEmpty(l.OpeningHour) || string.IsNullOrEmpty(l.ClosingHour)) return true;

            if (TimeSpan.TryParse(l.OpeningHour, out TimeSpan openTime) &&
                TimeSpan.TryParse(l.ClosingHour, out TimeSpan closeTime))
            {
                TimeSpan actualCloseTime = closeTime;

                // If the closing time is a smaller number than the opening time (e.g., 01:30 < 07:00),
                // it means the location is open past midnight into the next day. We add 24 hours to fix the math.
                if (closeTime <= openTime)
                {
                    actualCloseTime = closeTime.Add(TimeSpan.FromHours(24));
                }

                TimeSpan finishTime = current.Add(TimeSpan.FromMinutes(l.AvgDuration));

                return current >= openTime && finishTime <= actualCloseTime;
            }

            return true;
        }

        // Helper: Calculates straight-line distance (in meters) between two GPS coordinates using the Haversine formula
        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3; // Earth radius in meters
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }
        private bool FitsBeforeNextEvent(Location l, TimeSpan current, Reservation nextRes, TimeSpan endLimit)
        {
            var finishTime = current.Add(TimeSpan.FromMinutes(l.AvgDuration));

            if (nextRes != null)
            {
                var arrivalDeadLine = nextRes.StartDate.TimeOfDay.Subtract(TimeSpan.FromMinutes(15));
                if (finishTime > arrivalDeadLine) return false;
            }

            return finishTime <= endLimit;
        }

        // ------------------------------------------------------------------
        // NEW: REAL-WORLD ROUTING LOGIC (Min of Walking vs Driving)
        // ------------------------------------------------------------------
        private async Task<(Location Location, int TravelTime)> FindBestNextLocationAsync(
            Location lastLocation,
            List<Location> destinations,
            TimeSpan currentTime,
            Reservation nextRes,
            TimeSpan endTimeLimit)
        {
            // If it's the first location of the day, there is no previous travel time. 
            // Just pick the closest one that fits the schedule.
            if (lastLocation == null)
            {
                var firstLoc = destinations.FirstOrDefault(l => IsLocationOpen(l, currentTime) && FitsBeforeNextEvent(l, currentTime, nextRes, endTimeLimit));
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

                // Fetch Walking Times
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

                // Fetch Driving Times
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
                int matrixIndex = i + 1; // 0 is the origin itself

                double walkSecs = walkingDurations != null && matrixIndex < walkingDurations.Count ? walkingDurations[matrixIndex] : double.MaxValue;
                double driveSecs = drivingDurations != null && matrixIndex < drivingDurations.Count ? drivingDurations[matrixIndex] : double.MaxValue;

                // Pick the fastest mode of transport!
                double minSecs = Math.Min(walkSecs, driveSecs);

                // Failsafe: If Mapbox failed, estimate using 10km/h city speed (approx 166 meters/min)
                if (minSecs == double.MaxValue)
                {
                    minSecs = Math.Max(5 * 60, (GetDistance(lastLocation.Latitude, lastLocation.Longitude, candidate.Latitude, candidate.Longitude) / 166.0) * 60);
                }

                int travelMinutes = (int)Math.Ceiling(minSecs / 60.0);

                // CRUCIAL: Add the travel time to the current clock to see if it's still open when we actually arrive!
                TimeSpan projectedArrival = currentTime.Add(TimeSpan.FromMinutes(travelMinutes));

                if (IsLocationOpen(candidate, projectedArrival) && FitsBeforeNextEvent(candidate, projectedArrival, nextRes, endTimeLimit))
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

        // Helper classes for JSON deserialization
        public class MapboxMatrixResponse
        {
            public string code { get; set; }
            // Durations are in seconds
            public List<List<double>> durations { get; set; }
            // Distances are in meters
            public List<List<double>> distances { get; set; }
        }


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

                // 1. Get user preferences
                var topTags = await _context.UserTagPreferences
                    .Include(p => p.Tag)
                    .Where(p => p.UserId == userId && p.RankOrder <= 3)
                    .OrderBy(p => p.RankOrder)
                    .Select(p => p.Tag)
                    .ToListAsync();

                if (!topTags.Any()) return Json(new { success = false, message = "Please rank your preferences first!" });

                var mapboxToken = _configuration["Mapbox:PublicKey"];
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TravelPlannerLicenta");

                // 🔴 MAKE SURE YOUR KEY IS HERE
                string groqApiKey = "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";
                Uri groqEndpoint = new Uri("https://api.groq.com/openai/v1/chat/completions");

                var addedLocations = new List<Location>();
                List<AiLocationSuggestion> recommendedPlaces = new List<AiLocationSuggestion>();

                // ====================================================================
                // SHIELD LAYER 1: Resolve City Center Coordinates for Proximity Bias
                // ====================================================================
                double cityLng = 0;
                double cityLat = 0;
                string cityCenterUrl = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{Uri.EscapeDataString($"{trip.City}, {trip.Country}")}.json?types=place,locality&access_token={mapboxToken}";

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

                // ====================================================================
                // STEP 2: THE AI BRAIN
                // ====================================================================
                string tagsListString = string.Join(", ", topTags.Select(t => t.Name));

                var aiRequestPayload = new
                {
                    model = "llama-3.3-70b-versatile",
                    temperature = 0.1,
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = "You are a travel API. Output ONLY raw JSON. Your output must exactly match this schema: {\"attractions\": [{\"name\": \"Exact Name\", \"tag\": \"Category\"}]}" },
                        new { role = "user", content = $"Give me the 8 most famous, actual visitable tourist attractions in {trip.City}, {trip.Country}. Prioritize these categories: {tagsListString}. Do NOT include neighborhoods, hotels, or restaurants. Use their official real-world names." }
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

                        using var contentDoc = JsonDocument.Parse(rawContentJson);
                        var attractionsArray = contentDoc.RootElement.GetProperty("attractions");

                        recommendedPlaces = System.Text.Json.JsonSerializer.Deserialize<List<AiLocationSuggestion>>(attractionsArray.GetRawText());
                    }
                    else
                    {
                        string errorMsg = await aiResponse.Content.ReadAsStringAsync();
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

                // ====================================================================
                // STEP 3: MAPBOX GEOCODER WITH BIAS & MULTI-FIELD ADDRESS VALIDATION
                // ====================================================================
                foreach (var item in recommendedPlaces)
                {
                    if (addedLocations.Count >= 8) break;

                    string geocodeQuery = $"{item.name}, {trip.City}";
                    string url = $"https://api.mapbox.com/search/searchbox/v1/forward?q={Uri.EscapeDataString(geocodeQuery)}&poi_category=tourist_attraction,museum,historic,monument,park,place_of_worship,castle&access_token={mapboxToken}&limit=4";

                    // Apply proximity bias if we successfully located the city center coordinates
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

                            string lowerName = validatedName.ToLower();
                            if (lowerName.Contains("hotel") || lowerName.Contains("apartment") ||
                                lowerName.Contains("studio") || lowerName.Contains("bedroom") ||
                                lowerName.Contains("parking") || lowerName.Contains("yespark") ||
                                lowerName.Contains("suite") || lowerName.Contains("room"))
                            {
                                continue;
                            }

                            // 🌟 SHIELD LAYER 2: Multi-Field Address Verification Check
                            string fullAddress = props.TryGetProperty("full_address", out var addr) ? (addr.GetString() ?? "") : "";
                            string placeFormatted = props.TryGetProperty("place_formatted", out var pf) ? (pf.GetString() ?? "") : "";

                            // Confirm the metadata mentions BOTH the city and country parameters requested
                            bool matchesCity = fullAddress.Contains(trip.City, StringComparison.OrdinalIgnoreCase) ||
                                               placeFormatted.Contains(trip.City, StringComparison.OrdinalIgnoreCase);

                            bool matchesCountry = fullAddress.Contains(trip.Country, StringComparison.OrdinalIgnoreCase) ||
                                                  placeFormatted.Contains(trip.Country, StringComparison.OrdinalIgnoreCase);

                            // If it fails to map to your destination, drop it out immediately!
                            if (!matchesCity || !matchesCountry)
                            {
                                continue;
                            }

                            bool isDuplicate = trip.Locations.Any(l => l.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase)) ||
                                               addedLocations.Any(l => l.Name.Equals(validatedName, StringComparison.OrdinalIgnoreCase));

                            if (!isDuplicate)
                            {
                                var coords = feature.GetProperty("geometry").GetProperty("coordinates");
                                var targetTag = topTags.FirstOrDefault(t => t.Name.Equals(item.tag, StringComparison.OrdinalIgnoreCase)) ?? topTags.First();

                                var newLoc = new Location
                                {
                                    TripId = tripId,
                                    Name = validatedName,
                                    City = trip.City,
                                    Address = !string.IsNullOrEmpty(fullAddress) ? fullAddress : placeFormatted,
                                    Latitude = coords[1].GetDouble(),
                                    Longitude = coords[0].GetDouble(),
                                    AvgDuration = 90,
                                    IsIndoor = true
                                };

                                _context.Locations.Add(newLoc);
                                addedLocations.Add(newLoc);
                                _context.LocationTags.Add(new LocationTag { Location = newLoc, TagId = targetTag.TagId });
                                break;
                            }
                        }
                    }
                    catch { }
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

        // Helper class needed to read the AI's JSON
        public class AiLocationSuggestion
        {
            public string name { get; set; }
            public string tag { get; set; }
        }
    }
}