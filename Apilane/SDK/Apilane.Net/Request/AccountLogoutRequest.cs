using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class AccountLogoutRequest : ApilaneRequestBase
    {
        public static AccountLogoutRequest New(bool logOutFromEverywhere) => new(logOutFromEverywhere);

        private bool _logOutFromEverywhere;

        private AccountLogoutRequest(bool logOutFromEverywhere) : base(null, "Account", "Logout")
        {
            _logOutFromEverywhere = logOutFromEverywhere;
        }

        public AccountLogoutRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "everywhere", _logOutFromEverywhere.ToString().ToLower() }
            };

            return extraParams;
        }
    }
}
