namespace Apilane.Net.Request
{
    public class AccountUpdateRequest : ApilaneRequestBase
    {
        public static AccountUpdateRequest New() => new();

        private AccountUpdateRequest() : base(null, "Account", "Update")
        {

        }

        public AccountUpdateRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }
    }
}
