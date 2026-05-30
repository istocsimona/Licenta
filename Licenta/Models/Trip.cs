using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    // Add IValidatableObject to the class definition
    public class Trip : IValidatableObject
    {
        [Key]
        public int TripId { get; set; }
        public string? UserId { get; set; }

        [Required(ErrorMessage = "The title is required")]
        public string Title { get; set; }

        public string Description { get; set; }

        [Required(ErrorMessage = "The start date is required")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "The end date is required")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required(ErrorMessage = "The city is required")]
        public string City { get; set; }

        [Required(ErrorMessage = "The country is required")]
        public string Country { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Range(0, 24)]
        public int StartExplorationHour { get; set; } = 8;

        [Range(0, 24)]
        public int EndExplorationHour { get; set; } = 20;

        // --- NAVIGATION PROPERTIES ---
        public virtual ApplicationUser? User { get; set; }
        public virtual Accommodation? Accommodation { get; set; }
        public virtual ICollection<DayPlan>? DayPlans { get; set; }
        public virtual ICollection<Location> Locations { get; set; } = new List<Location>();
        public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();


        // --- NEW: Server-side custom validation ---
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Check if StartDate is in the past (ignoring time)
            if (StartDate.Date < DateTime.Now.Date)
            {
                yield return new ValidationResult("The start date cannot be in the past.", new[] { nameof(StartDate) });
            }

            // Check if EndDate is before StartDate
            if (EndDate.Date < StartDate.Date)
            {
                yield return new ValidationResult("The end date must be on or after the start date.", new[] { nameof(EndDate) });
            }
        }
    }
}