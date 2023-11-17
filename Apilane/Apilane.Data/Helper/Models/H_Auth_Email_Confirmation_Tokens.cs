namespace Apilane.Data.Helper.Models
{
    public class H_Auth_Email_Confirmation_Tokens
    {
        public long ID { get; set; }
        public long Owner { get; set; }
        public string Token { get; set; } = null!;
        public long Created { get; set; }
    }
}
