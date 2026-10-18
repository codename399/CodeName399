using DatabaseRespository.Model;

namespace CodeName399.Model
{
    public class AuthenticationDatabaseSetting : MongoDatabaseSetting
    {
        public string? UserCollection { get; set; }
        public string? RoleCollection { get; set; }
        public string? ProjectCollection { get; set; }
        public string? UserProjectMappingCollection { get; set; }
        public string? RefreshTokenCollection { get; set; }
    }
}
