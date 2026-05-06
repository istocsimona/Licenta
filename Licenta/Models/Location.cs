using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class Location
    {
        [Key]
        public int LocationId { get; set; }
        public int TripId { get; set; }
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int AvgDuration { get; set; }
        public string? OpeningHour { get; set; }
        public string? ClosingHour { get; set; }
        public bool IsIndoor { get; set; }

        public virtual Trip Trip { get; set; }
        public virtual ICollection<LocationTag> LocationTags { get; set; } = new List<LocationTag>();
    }
}