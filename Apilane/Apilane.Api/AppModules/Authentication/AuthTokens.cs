namespace Apilane.Common.Models.AppModules.Authentication
{
    public class AuthTokens
    {
        public long ID { get; set; }
        public long Owner { get; set; }
        public string Token { get; set; } = null!;
        public long Created { get; set; }
    }
}
