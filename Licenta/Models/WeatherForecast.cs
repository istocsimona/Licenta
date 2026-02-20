using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class WeatherForecast
    {
        [Key]
        public int WeatherId { get; set; }
        public int TripId { get; set; }
        public string Icon { get; set; }
        public DateTime Date { get; set; }
        public string Condition { get; set; }
        public double Temperature { get; set; }
    }
}
