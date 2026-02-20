using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class ItineraryItem
    {
        [Key]
        public int ItineraryItemId { get; set; }
        public int DayPlanId { get; set; }
        public int LocationId { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int OrderIndex { get; set; }
        public string VisitStatus { get; set; }

        public virtual DayPlan DayPlan { get; set; }
        public virtual Location Location { get; set; }
    }
}
