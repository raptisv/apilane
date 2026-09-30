using Apilane.Net.Models.Account;

namespace Apilane.Net.Request
{
    /// <summary>
    /// Changes the authenticated user's password. Authenticate with <c>WithAuthToken</c> or
    /// <c>WithSigning</c>; the current password is verified server-side before the new one
    /// (8 to 400 characters) is stored.
    /// </summary>
    public class AccountChangePasswordRequest : ApilaneRequestBase<AccountChangePasswordRequest>
    {
        private readonly ChangePasswordItem _changePasswordItem;

        public ChangePasswordItem ChangePasswordItem => _changePasswordItem;

        public static AccountChangePasswordRequest New(ChangePasswordItem changePasswordItem) => new(changePasswordItem);

        private AccountChangePasswordRequest(ChangePasswordItem changePasswordItem) : base(null, "Account", "ChangePassword")
        {
            _changePasswordItem = changePasswordItem ?? throw new System.ArgumentNullException(nameof(changePasswordItem));
        }
    }
}
