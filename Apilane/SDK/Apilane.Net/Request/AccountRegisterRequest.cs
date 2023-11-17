namespace Apilane.Net.Request
{
    public class AccountRegisterRequest : ApilaneRequestBase
    {
        public static AccountRegisterRequest New() => new();

        private AccountRegisterRequest() : base(null, "Account", "Register")
        {

        }
    }
}
