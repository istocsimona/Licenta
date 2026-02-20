using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class DayPlan
    {
        [Key]
        public int DayPlanId { get; set; }
        public int TripId { get; set; }
        public DateTime Date { get; set; }
        public string? Notes { get; set; }
        public int VisitedLocationCount { get; set; }

        public virtual Trip Trip { get; set; }
        public virtual ICollection<ItineraryItem> ItineraryItems { get; set; }
    }
}
