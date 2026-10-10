namespace Apilane.Net.Request
{
    /// <summary>
    /// Sets a new password with the token of the reset email: the value of the
    /// <c>{reset_password_token}</c> placeholder of the template. The token and the password travel in the
    /// request body, never in the address. Anonymous: it needs the application token only.
    /// </summary>
    public class EmailResetPasswordRequest : ApilaneRequestBase<EmailResetPasswordRequest>
    {
        public static EmailResetPasswordRequest New(string token, string newPassword) => new(token, newPassword);

        public string Token { get; }

        public string NewPassword { get; }

        private EmailResetPasswordRequest(string token, string newPassword) : base(null, "Email", "ResetPassword")
        {
            Token = token;
            NewPassword = newPassword;
        }
    }
}
