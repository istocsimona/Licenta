using Microsoft.AspNetCore.Identity;

namespace Licenta.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? ProfilePicture { get; set; }

        // Relații conform diagramă
        public virtual ICollection<Trip> Trips { get; set; }
        public virtual ICollection<UserTagPreference> TagPreferences { get; set; }
    }

}
