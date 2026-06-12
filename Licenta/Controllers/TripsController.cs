using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Licenta.Controllers
{
    public class TripsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public TripsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Trips
        [Authorize]
        public async Task<IActionResult> Index()
        {
            // 1. Fetch user trips
            var currentUserId = _userManager.GetUserId(User);
            var userTrips = await _context.Trips
                .Where(t => t.UserId == currentUserId)
                .ToListAsync();

            // 2. Set fallback messages
            ViewBag.TriviaQuestion = "Travel trivia currently unavailable.";
            ViewBag.TriviaAnswer = "";

            // 3. Make the API call to Open Trivia DB
            try
            {
                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "https://opentdb.com/api.php?amount=1&category=22");

                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        using (var doc = JsonDocument.Parse(jsonString))
                        {
                            var root = doc.RootElement;

                            if (root.GetProperty("response_code").GetInt32() == 0)
                            {
                                var result = root.GetProperty("results")[0];

                                string question = result.GetProperty("question").GetString();
                                string correctAnswer = result.GetProperty("correct_answer").GetString();

                                // 1. Decode the question and correct answer
                                ViewBag.TriviaQuestion = WebUtility.HtmlDecode(question);
                                ViewBag.TriviaAnswer = WebUtility.HtmlDecode(correctAnswer);

                                // 2. Grab the incorrect answers and decode them
                                var options = new List<string>();
                                foreach (var wrongAnswer in result.GetProperty("incorrect_answers").EnumerateArray())
                                {
                                    options.Add(WebUtility.HtmlDecode(wrongAnswer.GetString()));
                                }

                                // 3. Add the correct answer to the list of options
                                options.Add(ViewBag.TriviaAnswer);

                                // 4. Shuffle the options so the correct answer isn't always last
                                var random = new Random();
                                ViewBag.TriviaOptions = options.OrderBy(x => random.Next()).ToList();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fails silently if internet connection drops
            }

            return View(userTrips);
        }

        // GET: Trips/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var trip = await _context.Trips
             .Include(t => t.Accommodation)
            .Include(t => t.Locations)
                .ThenInclude(l => l.LocationTags)
                    .ThenInclude(lt => lt.Tag) 
            .Include(t => t.Reservations)
            .Include(t => t.DayPlans)
                .ThenInclude(dp => dp.ItineraryItems)
                    .ThenInclude(ii => ii.Location) 
            .FirstOrDefaultAsync(m => m.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            // Make sure the logged-in user owns this trip
            if (trip.UserId != _userManager.GetUserId(User))
            {
                return Forbid();
            }

            return View(trip);
        }

        // GET: Trips/Create
        public IActionResult Create()
        {
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id");
            return View();
        }

        // POST: Trips/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Title,Description,StartDate,EndDate,City,Country,StartExplorationHour,EndExplorationHour")] Trip trip)
        {
            // Setăm automat ID-ul utilizatorului curent
            trip.UserId = _userManager.GetUserId(User);
            trip.CreatedAt = DateTime.Now;

            // Eliminăm UserId din verificarea validării deoarece îl setăm manual mai sus
            ModelState.Remove("UserId");

            if (ModelState.IsValid)
            {
                _context.Add(trip);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(trip);
        }

        // GET: Trips/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var trip = await _context.Trips.FindAsync(id);
            if (trip == null) return NotFound();

            // Este acesta trip-ul utilizatorului logat?
            if (trip.UserId != _userManager.GetUserId(User))
            {
                return Forbid();
            }

            // A inceput deja excursia? Daca da, interzicem editarea.
            if (trip.StartDate <= DateTime.Now.Date)
            {
                // Poti returna un mesaj de eroare custom sau pur si simplu Forbid/Redirect
                TempData["ErrorMessage"] = "You cannot edit a trip that has already started or passed.";
                return RedirectToAction(nameof(Index));
            }

            return View(trip);
        }

        // POST: Trips/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
        [Bind("TripId,Title,Description,StartDate,EndDate,City,Country,StartExplorationHour,EndExplorationHour")] Trip trip)
        {
            if (id != trip.TripId)
                return NotFound();

            var tripFromDb = await _context.Trips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (tripFromDb == null)
                return NotFound();

            //  Restore protected fields
            trip.UserId = tripFromDb.UserId;
            trip.CreatedAt = tripFromDb.CreatedAt;

            // Security check 1: Ownership
            if (trip.UserId != _userManager.GetUserId(User))
                return Forbid();

            // Security check 2: Prevent POST bypass if trip has started
            if (tripFromDb.StartDate <= DateTime.Now.Date)
            {
                TempData["ErrorMessage"] = "You cannot edit a trip that has already started.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
                return View(trip);

            _context.Update(trip);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }



        // POST: Trips/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var trip = await _context.Trips.FindAsync(id);
            if (trip != null)
            {
                _context.Trips.Remove(trip);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TripExists(int id)
        {
            return _context.Trips.Any(e => e.TripId == id);
        }



        // GET: Trips/Calendar
        [Authorize]
        public async Task<IActionResult> Calendar()
        {
            var currentUserId = _userManager.GetUserId(User);
            var userTrips = await _context.Trips
                .Where(t => t.UserId == currentUserId)
                .ToListAsync();

            return View(userTrips);
        }


        
        // GET: Trips/ScratchMap
        [Authorize]
        public async Task<IActionResult> ScratchMap()
        {
            var currentUserId = _userManager.GetUserId(User);
            var today = DateTime.Now.Date;

            // 1. Visited: Trips that have already started/happened
            var visitedCountries = await _context.Trips
                .Where(t => t.UserId == currentUserId && !string.IsNullOrEmpty(t.Country) && t.StartDate <= today)
                .Select(t => t.Country.Trim())
                .Distinct()
                .ToListAsync();

            // 2. Future: Trips planned for the future
            var futureCountries = await _context.Trips
                .Where(t => t.UserId == currentUserId && !string.IsNullOrEmpty(t.Country) && t.StartDate > today)
                .Select(t => t.Country.Trim())
                .Distinct()
                .ToListAsync();

            // Pass both lists to the view
            ViewBag.VisitedCountries = visitedCountries;
            ViewBag.FutureCountries = futureCountries;

            return View();
        }

        public IActionResult PastTrips()
        {
            var today = DateTime.Now.Date;

            // Fetch only past trips from your database (assuming _context.Trips)
            var pastTrips = _context.Trips
                .Where(t => t.EndDate.Date < today)
                .OrderByDescending(t => t.EndDate)
                .ToList();

            return View(pastTrips);
        }
    }

}
