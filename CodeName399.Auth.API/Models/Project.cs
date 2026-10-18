using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Models
{
    public class Project : Audit
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Route { get; set; }
        public string? NavigationText { get; set; }
        public bool IsAdmin { get; set; }

        public Project()
        {
            Description = "Custom Application";
            NavigationText = "Start";
            IsAdmin = false;
        }
    }
}
