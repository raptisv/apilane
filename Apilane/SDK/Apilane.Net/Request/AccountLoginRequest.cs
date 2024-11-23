using Apilane.Net.Models.Account;

namespace Apilane.Net.Request
{
    public class AccountLoginRequest : ApilaneRequestBase<AccountLoginRequest>
    {
        private LoginItem _loginItem;
        public LoginItem LoginItem => _loginItem;
        
        public static AccountLoginRequest New(LoginItem loginItem) => new(loginItem);

        private AccountLoginRequest(LoginItem loginItem) : base(null, "Account", "Login")
        {
            _loginItem = loginItem;
        }
    }
}
