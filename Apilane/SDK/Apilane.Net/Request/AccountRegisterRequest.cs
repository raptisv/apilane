using Apilane.Net.Models.Account;

namespace Apilane.Net.Request
{
    public class AccountRegisterRequest : ApilaneRequestBase<AccountRegisterRequest>
    {
        private IRegisterItem _registerItem;
        public IRegisterItem RegisterItem => _registerItem;

        public static AccountRegisterRequest New(IRegisterItem registerItem) => new(registerItem);

        private AccountRegisterRequest(IRegisterItem registerItem) : base(null, "Account", "Register")
        {
            _registerItem = registerItem;
        }
    }
}
