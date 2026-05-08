using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System;

namespace Licenta.Controllers // Ensure this matches your namespace
{
    [Authorize]
    public class ReservationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReservationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [FromForm] int tripId,
            [FromForm] string name,
            [FromForm] string location,
            [FromForm] DateTime startDate,
            [FromForm] DateTime endDate,
            [FromForm] double? latitude, // NEW: Capture Mapbox Latitude
            [FromForm] double? longitude) // NEW: Capture Mapbox Longitude
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    TempData["ErrorMessage"] = "You must be logged in to create an event.";
                    return RedirectToAction("Index", "Trips");
                }

                var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);

                if (trip == null)
                {
                    TempData["ErrorMessage"] = "Trip not found.";
                    return RedirectToAction("Index", "Trips");
                }

                // --- NEW LOGIC: ADD TO ITINERARY IF IT HAS COORDINATES ---
                if (latitude.HasValue && longitude.HasValue)
                {
                    // Create a tiny bounding box to check if this location is already in the trip
                    // This prevents EF Core translation errors that happen with Math.Abs()
                    var latMin = latitude.Value - 0.0001;
                    var latMax = latitude.Value + 0.0001;
                    var lngMin = longitude.Value - 0.0001;
                    var lngMax = longitude.Value + 0.0001;

                    bool locationExists = await _context.Locations.AnyAsync(l =>
                        l.TripId == tripId &&
                        l.Latitude >= latMin && l.Latitude <= latMax &&
                        l.Longitude >= lngMin && l.Longitude <= lngMax);

                    // If it's a completely new location, add it to the map itinerary!
                    if (!locationExists)
                    {
                        var newLocation = new Location
                        {
                            TripId = tripId,
                            Name = location,
                            Latitude = latitude.Value,
                            Longitude = longitude.Value,
                            AvgDuration = 60, // Default time
                            IsIndoor = false  // Default
                        };
                        _context.Locations.Add(newLocation);
                    }
                }
                // ---------------------------------------------------------

                var reservation = new Reservation
                {
                    TripId = tripId,
                    UserId = userId,
                    Name = name,
                    Location = location,
                    StartDate = startDate,
                    EndDate = endDate
                };

                _context.Reservations.Add(reservation);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Event added successfully!";
                return RedirectToAction("Index", "Trips");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Database Error: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToAction("Index", "Trips");
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken] // Keep this for AJAX if not passing tokens
        public async Task<IActionResult> AjaxDeleteReservation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Explicitly check for ReservationId
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.ReservationId == id && r.UserId == userId);

            if (reservation != null)
            {
                _context.Reservations.Remove(reservation);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Reservation not found or access denied." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int tripId)
        {
            try
            {
                // Find the reservation
                var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.ReservationId == id);

                if (reservation != null)
                {
                    // Remove it from the database
                    _context.Reservations.Remove(reservation);
                    await _context.SaveChangesAsync();
                }

                // Redirect seamlessly back to the Trip Details page
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
            catch (Exception)
            {
                // Fallback in case of database error
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }
    }
}