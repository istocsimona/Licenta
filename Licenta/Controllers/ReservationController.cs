using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System;
using System.Threading.Tasks;

namespace Licenta.Controllers
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
        [FromForm] int locationId, 
        [FromForm] DateTime startDate,
        [FromForm] DateTime endDate)
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

                // Create the reservation and link it directly to the existing Location row
                var reservation = new Reservation
                {
                    TripId = tripId,
                    UserId = userId,
                    Name = name,
                    LocationId = locationId, 
                    StartDate = startDate,
                    EndDate = endDate
                };

                _context.Reservations.Add(reservation);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Event added successfully!";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Database Error: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AjaxDeleteReservation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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
                var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.ReservationId == id);

                if (reservation != null)
                {
                    _context.Reservations.Remove(reservation);
                    await _context.SaveChangesAsync();
                }

                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
            catch (Exception)
            {
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }
    }
}