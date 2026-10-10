namespace Apilane.Net.Request
{
    /// <summary>
    /// Confirms the email of a user with the token of the confirmation email: the value of the
    /// <c>{confirmation_token}</c> placeholder of the template. The token travels in the request body,
    /// never in the address. Anonymous: it needs the application token only.
    /// </summary>
    public class EmailConfirmEmailRequest : ApilaneRequestBase<EmailConfirmEmailRequest>
    {
        public static EmailConfirmEmailRequest New(string token) => new(token);

        public string Token { get; }

        private EmailConfirmEmailRequest(string token) : base(null, "Email", "ConfirmEmail")
        {
            Token = token;
        }
    }
}
