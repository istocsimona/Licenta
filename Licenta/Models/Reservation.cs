using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class Reservation
    {
        [Key]
        public int ReservationId { get; set; }
        public int TripId { get; set; }
        public string UserId { get; set; }
        public string Location { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
