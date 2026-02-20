using System.ComponentModel.DataAnnotations;

namespace Licenta.Models
{
    public class Tag
    {
        [Key]
        public int TagId { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool IsDefault { get; set; }
        public string Color { get; set; }
    }
}
