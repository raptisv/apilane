namespace Apilane.Net.Request
{
    public class AccountRegisterRequest : ApilaneRequestBase<AccountRegisterRequest>
    {
        public static AccountRegisterRequest New() => new();

        private AccountRegisterRequest() : base(null, "Account", "Register")
        {

        }
    }
}
