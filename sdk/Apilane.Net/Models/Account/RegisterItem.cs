namespace Apilane.Net.Models.Account
{
    public interface IRegisterItem
    {
        string Email { get; set; }
        string Username { get; set; }
        string Password { get; set; }
    }

    public class RegisterItem : IRegisterItem
    {
        public string Email { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}
