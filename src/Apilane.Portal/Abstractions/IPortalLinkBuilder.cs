namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Absolute links to pages of the Portal UI, for mails sent by the management API.
    /// The address is the one the current request came in on.
    /// </summary>
    public interface IPortalLinkBuilder
    {
        /// <summary>
        /// The absolute URL of a UI page, e.g. Ui("account/login").
        /// </summary>
        string Ui(string path);

        /// <summary>
        /// The page where a user sets a new password with the code of a reset mail.
        /// </summary>
        string ResetPassword(string code);

        /// <summary>
        /// The page where a user asks for a password-reset mail.
        /// </summary>
        string ForgotPassword();
    }
}
