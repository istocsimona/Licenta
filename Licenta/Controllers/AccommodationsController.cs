using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Licenta.Controllers
{
    [Authorize]
    public class AccommodationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccommodationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: Accommodations/CreateModal
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateModal([Bind("TripId,Name,Address,Latitude,Longitude")] Accommodation accommodation)
        {
            if (ModelState.IsValid)
            {
                _context.Add(accommodation);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "Trips", new { id = accommodation.TripId });
        }
        // POST: Accommodations/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int tripId)
        {
            var accommodation = await _context.Accommodations.FindAsync(id);
            if (accommodation != null)
            {
                _context.Accommodations.Remove(accommodation);
                await _context.SaveChangesAsync();
            }

            // Send the user right back to the Trip Details page
            return RedirectToAction("Details", "Trips", new { id = tripId });
        }
    }
}