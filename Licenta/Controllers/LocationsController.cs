using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Licenta.Controllers
{
    [Authorize]
    public class LocationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LocationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Locations/Index
        public IActionResult Index()
        {
            return View();
        }

        // GET: Locations/Add?tripId=5
        public async Task<IActionResult> Add(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
            {
                return NotFound();
            }

            return View(trip); // We pass the Trip model to the view
        }
    }
}