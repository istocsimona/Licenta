namespace Licenta.Models
{
    public class UserTagPreference
    {
        public string UserId { get; set; }
        public int TagId { get; set; }
        public int RankOrder { get; set; }

        public virtual ApplicationUser User { get; set; }
        public virtual Tag Tag { get; set; }
    }
}
