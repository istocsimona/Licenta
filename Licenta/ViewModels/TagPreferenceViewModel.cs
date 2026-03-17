namespace Licenta.ViewModels
{
    public class TagPreferenceViewModel
    {
        // Data from the Tag model
        public int TagId { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public string Icon { get; set; }

        // Data from the UserTagPreference model (nullable because they might not have ranked it yet)
        public int? RankOrder { get; set; }
    }
}