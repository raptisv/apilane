using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class AccountConfirmationEmailRequest : ApilaneRequestBase<AccountConfirmationEmailRequest>
    {
        public static AccountConfirmationEmailRequest New(string email) => new(email);

        private readonly string _email;

        private AccountConfirmationEmailRequest(string email) : base(null, "Email", "RequestConfirmation")
        {
            _email = email;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection();

            if (!string.IsNullOrWhiteSpace(_email))
            {
                extraParams.Add("email", _email);
            }

            return extraParams;
        }
    }
}
