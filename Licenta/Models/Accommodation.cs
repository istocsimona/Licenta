using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Licenta.Models
{
    public class Accommodation
    {
        [Key]
        public int AccommodationId { get; set; }

        public int TripId { get; set; }

        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; }

        public string Address { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string? Details { get; set; }

        // --- NAVIGATION PROPERTY ---
        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }
    }
}