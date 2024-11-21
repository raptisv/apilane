namespace Apilane.Net.Request
{
    public class AccountUpdateRequest : ApilaneRequestBase<AccountUpdateRequest>
    {
        public static AccountUpdateRequest New() => new();

        private AccountUpdateRequest() : base(null, "Account", "Update")
        {

        }
    }
}
