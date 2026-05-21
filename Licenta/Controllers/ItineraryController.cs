using Licenta.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Licenta.Controllers
{
    public class ItineraryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ItineraryController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> GenerateItinerary(int tripId)
        {
            try
            {
                var trip = await _context.Trips
                    .Include(t => t.Locations)
                    .Include(t => t.Reservations)
                    .FirstOrDefaultAsync(t => t.TripId == tripId);

                if (trip == null) return Json(new { success = false, message = "Trip not found" });

                // Clean existing itinerary items to allow fresh regeneration
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

                // FIX: Match using the official LocationId instead of comparing string names!
                var availableLocations = tripLocations
                    .Where(l => !tripReservations.Any(r => r.LocationId == l.LocationId))
                    .ToList();

                // Loop through each day of the trip
                for (var date = trip.StartDate.Date; date <= trip.EndDate.Date; date = date.AddDays(1))
                {
                    var dayPlan = new DayPlan { TripId = tripId, Date = date, ItineraryItems = new List<ItineraryItem>() };

                    TimeSpan currentTime = TimeSpan.FromHours(trip.StartExplorationHour);
                    TimeSpan endTimeLimit = TimeSpan.FromHours(trip.EndExplorationHour);

                    // Get remaining reservations specifically for this individual day
                    var todaysReservations = tripReservations
                        .Where(r => r.StartDate.Date == date)
                        .OrderBy(r => r.StartDate).ToList();

                    int orderCounter = 1;

                    while (currentTime < endTimeLimit)
                    {
                        // Grab the immediate next reservation coming up today
                        var nextRes = todaysReservations.FirstOrDefault();

                        // Enforce the 15-minute early buffer rule for reservations
                        if (nextRes != null && currentTime >= nextRes.StartDate.TimeOfDay.Subtract(TimeSpan.FromMinutes(15)))
                        {
                            // FIX: Only add to itinerary if the reservation is successfully linked to a location
                            if (nextRes.LocationId.HasValue)
                            {
                                dayPlan.ItineraryItems.Add(new ItineraryItem
                                {
                                    LocationId = nextRes.LocationId.Value,
                                    StartTime = nextRes.StartDate.TimeOfDay, 
                                    EndTime = nextRes.EndDate.TimeOfDay,
                                    VisitStatus = "Reservation",
                                    OrderIndex = orderCounter++
                                });
                            }

                            currentTime = nextRes.EndDate.TimeOfDay;
                            todaysReservations.Remove(nextRes); // Complete execution tracking
                            continue;
                        }

                        // Try to find a standard location that comfortably fits before the next ticket's early cutoff window
                        var bestLocation = availableLocations.FirstOrDefault(l =>
                            IsLocationOpen(l, currentTime) &&
                            FitsBeforeNextEvent(l, currentTime, nextRes, endTimeLimit));

                        if (bestLocation != null)
                        {
                            dayPlan.ItineraryItems.Add(new ItineraryItem
                            {
                                LocationId = bestLocation.LocationId,
                                StartTime = currentTime,
                                EndTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration)),
                                VisitStatus = "Planned",
                                OrderIndex = orderCounter++
                            });

                            currentTime = currentTime.Add(TimeSpan.FromMinutes(bestLocation.AvgDuration + 15)); // 15m commute buffer
                            availableLocations.Remove(bestLocation);
                        }
                        else
                        {
                            // Fast-forward directly to the next reservation's buffer if we have dead time
                            if (nextRes != null)
                            {
                                currentTime = nextRes.StartDate.TimeOfDay.Subtract(TimeSpan.FromMinutes(15));
                            }
                            else
                            {
                                break; // End day timeline construction
                            }
                        }
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

        private bool IsLocationOpen(Location l, TimeSpan current)
        {
            if (string.IsNullOrEmpty(l.OpeningHour) || string.IsNullOrEmpty(l.ClosingHour)) return true;

            if (TimeSpan.TryParse(l.OpeningHour, out TimeSpan openTime) &&
                TimeSpan.TryParse(l.ClosingHour, out TimeSpan closeTime))
            {
                return current >= openTime && current.Add(TimeSpan.FromMinutes(l.AvgDuration)) <= closeTime;
            }

            return true;
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
    }
}