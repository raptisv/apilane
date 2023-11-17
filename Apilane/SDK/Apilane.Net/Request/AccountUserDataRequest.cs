namespace Apilane.Net.Request
{
    public class AccountUserDataRequest : ApilaneRequestBase
    {
        public static AccountUserDataRequest New() => new();

        private AccountUserDataRequest() : base(null, "Account", "UserData")
        {

        }

        public AccountUserDataRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }
    }
}
