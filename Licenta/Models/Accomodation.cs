using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class Accomodation
    {
        [Key]
        public int AccomodationId { get; set; }
        public int TripId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public string Details { get; set; }
    }
}
