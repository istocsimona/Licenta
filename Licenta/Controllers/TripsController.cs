using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Mono.TextTemplating;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            // Acum GetUserId va funcționa corect
            var currentUserId = _userManager.GetUserId(User);
            var userTrips = await _context.Trips
                .Where(t => t.UserId == currentUserId)
                .ToListAsync();

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
              .Include(t => t.User)
              .Include(t => t.Locations)
                .ThenInclude(l => l.LocationTags)
                .ThenInclude(lt => lt.Tag)
              .Include(t => t.Reservations)
              .FirstOrDefaultAsync(m => m.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            // SECURITY CHECK: Make sure the logged-in user owns this trip
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
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
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

            // VERIFICARE 1: Este acesta trip-ul utilizatorului logat?
            if (trip.UserId != _userManager.GetUserId(User))
            {
                return Forbid();
            }

            // VERIFICARE 2: A inceput deja excursia? Daca da, interzicem editarea.
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
    }

}
