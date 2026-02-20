namespace Licenta.Models
{
    public class LocationTag
    {
        public int LocationId { get; set; }
        public int TagId { get; set; }

        public virtual Location Location { get; set; }
        public virtual Tag Tag { get; set; }
    }
}
