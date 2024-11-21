namespace Apilane.Net.Request
{
    public class AccountUserDataRequest : ApilaneRequestBase<AccountUserDataRequest>
    {
        public static AccountUserDataRequest New() => new();

        private AccountUserDataRequest() : base(null, "Account", "UserData")
        {

        }
    }
}
