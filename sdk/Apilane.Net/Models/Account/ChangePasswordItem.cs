namespace Apilane.Net.Models.Account
{
    /// <summary>
    /// Body of the Account/ChangePassword call.
    /// </summary>
    public class ChangePasswordItem
    {
        /// <summary>
        /// The user's current password; verified server-side before the change is applied.
        /// </summary>
        public string Password { get; set; } = null!;

        /// <summary>
        /// The new password (8 to 400 characters).
        /// </summary>
        public string NewPassword { get; set; } = null!;
    }
}
