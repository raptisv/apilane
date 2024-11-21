namespace Apilane.Net.Request
{
    public class AccountRenewAuthTokenRequest : ApilaneRequestBase<AccountRenewAuthTokenRequest>
    {
        public static AccountRenewAuthTokenRequest New() => new();

        private AccountRenewAuthTokenRequest() : base(null, "Account", "RenewAuthToken")
        {

        }
    }
}
