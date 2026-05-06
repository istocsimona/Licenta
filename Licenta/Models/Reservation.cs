using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class Reservation
    {
        [Key]
        public int ReservationId { get; set; }
        public int TripId { get; set; }
        public string UserId { get; set; }

        [Required]
        public string Name { get; set; } // Added Name

        public string Location { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime StartDate { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime EndDate { get; set; }
    }
}
