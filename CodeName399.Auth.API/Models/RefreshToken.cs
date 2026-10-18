using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Models
{
    public class RefreshToken : Audit
    {
        public string UserId { get; set; }
        public string Token { get; set; }
        public DateTime ExpiryDate { get; set; }
    }
}
