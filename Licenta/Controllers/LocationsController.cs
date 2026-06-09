using Licenta.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System;

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

        public async Task<IActionResult> Add(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null) return NotFound();

            // Fetch all default tags to display as options or for auto-matching
            ViewBag.AvailableTags = await _context.Tags.ToListAsync();

            return View(trip);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateLocation(int locationId, Location model, int[] SelectedTagIds)
        {
            // Remove validation for navigation properties
            ModelState.Remove("Trip");
            ModelState.Remove("LocationTags");
            ModelState.Remove("OpeningHour");
            ModelState.Remove("ClosingHour");
            ModelState.Remove("Reservations");
            ModelState.Remove("City"); 

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { message = "Validation failed", errors = errors });
            }

            try
            {
                var existingLocation = await _context.Locations.FindAsync(locationId);
                if (existingLocation == null) return NotFound();

                // Update the location
                existingLocation.Name = model.Name;
                existingLocation.Address = model.Address;
                existingLocation.Latitude = model.Latitude;
                existingLocation.Longitude = model.Longitude;
                existingLocation.AvgDuration = model.AvgDuration;
                existingLocation.OpeningHour = model.OpeningHour;
                existingLocation.ClosingHour = model.ClosingHour;
                existingLocation.IsIndoor = model.IsIndoor;

                
                if (string.IsNullOrEmpty(existingLocation.City))
                {
                    var trip = await _context.Trips.FindAsync(existingLocation.TripId);
                    if (trip != null) existingLocation.City = trip.City;
                }

                // Update tags
                var existingTags = _context.LocationTags.Where(lt => lt.LocationId == locationId);
                _context.LocationTags.RemoveRange(existingTags);

                if (SelectedTagIds != null && SelectedTagIds.Length > 0)
                {
                    var locationTags = SelectedTagIds.Select(tagId => new LocationTag
                    {
                        LocationId = locationId,
                        TagId = tagId
                    }).ToList();
                    _context.LocationTags.AddRange(locationTags);
                }

                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveLocation(Location model, int[] SelectedTagIds)
        {
            
            ModelState.Remove("Trip");
            ModelState.Remove("LocationTags");
            ModelState.Remove("OpeningHour");
            ModelState.Remove("ClosingHour");
            ModelState.Remove("Reservations");
            ModelState.Remove("City"); 

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { message = "Validation failed", errors = errors });
            }

            try
            {
                // Fetch the trip to assign the correct city before saving
                var trip = await _context.Trips.FindAsync(model.TripId);
                if (trip != null)
                {
                    model.City = trip.City;
                }

                _context.Locations.Add(model);
                await _context.SaveChangesAsync();

                if (SelectedTagIds != null && SelectedTagIds.Length > 0)
                {
                    var locationTags = SelectedTagIds.Select(tagId => new LocationTag
                    {
                        LocationId = model.LocationId,
                        TagId = tagId
                    }).ToList();

                    _context.LocationTags.AddRange(locationTags);
                    await _context.SaveChangesAsync();
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTripLocations(int tripId)
        {
            var locations = await _context.Locations
                .Where(l => l.TripId == tripId)
                .Select(l => new {
                    l.LocationId,
                    l.Name,
                    l.Address,
                    l.Latitude,
                    l.Longitude,
                    l.OpeningHour,
                    l.ClosingHour,
                    l.AvgDuration,
                    l.IsIndoor,

                    EventDetails = _context.Reservations
                        .Where(r => r.LocationId == l.LocationId)
                        .Select(r => new {
                            Id = r.ReservationId,
                            Name = r.Name,
                            StartDate = r.StartDate,
                            EndDate = r.EndDate
                        })
                        .FirstOrDefault(),

                    Tags = _context.LocationTags
                        .Where(lt => lt.LocationId == l.LocationId)
                        .Select(lt => new { lt.Tag.Name, lt.Tag.Color, lt.Tag.Icon })
                        .ToList()
                })
                .ToListAsync();

            return Json(locations);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteLocation(int id)
        {
            var location = await _context.Locations.FindAsync(id);
            if (location == null) return NotFound();

            var tags = _context.LocationTags.Where(lt => lt.LocationId == id);
            _context.LocationTags.RemoveRange(tags);

            
            var associatedEvents = _context.Reservations
                .Where(r => r.LocationId == id);

            if (associatedEvents.Any())
            {
                _context.Reservations.RemoveRange(associatedEvents);
            }

            _context.Locations.Remove(location);

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}