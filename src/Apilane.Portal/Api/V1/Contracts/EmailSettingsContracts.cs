using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The SMTP settings an application sends its e-mails with (registration confirmation, password
    /// reset) and the page users land on after confirming their address. The mail password is never
    /// returned; HasMailPassword says whether it is set.
    /// </summary>
    public class EmailSettingsResponse
    {
        public string? MailServer { get; set; }

        public int? MailServerPort { get; set; }

        /// <summary>
        /// The sender address of the e-mails.
        /// </summary>
        public string? MailFromAddress { get; set; }

        /// <summary>
        /// The sender name of the e-mails.
        /// </summary>
        public string? MailFromDisplayName { get; set; }

        public string? MailUserName { get; set; }

        [Required]
        public bool HasMailPassword { get; set; }

        /// <summary>
        /// Where a user is sent after confirming their e-mail address. Null sends them to the
        /// Portal's default landing page. Used only while the e-mail confirmation template is enabled.
        /// </summary>
        public string? EmailConfirmationRedirectUrl { get; set; }

        /// <summary>
        /// Whether server, port, sender address, sender name, user name and password are all set.
        /// The API server sends no e-mail for the application until they are.
        /// </summary>
        [Required]
        public bool IsMailSetup { get; set; }
    }

    /// <summary>
    /// New e-mail settings of an application. Every value is written, except MailPassword: null
    /// (or left out) keeps the stored password. Empty text is stored as null.
    /// </summary>
    public class EmailSettingsRequest
    {
        public string? MailServer { get; set; }

        /// <summary>
        /// 1 to 65535. Null clears it.
        /// </summary>
        [Range(1, 65535, ErrorMessage = "Must be between 1 and 65535")]
        public int? MailServerPort { get; set; }

        public string? MailFromAddress { get; set; }

        public string? MailFromDisplayName { get; set; }

        public string? MailUserName { get; set; }

        /// <summary>
        /// Write-only. Null keeps the stored password, an empty string clears it, a value replaces it.
        /// </summary>
        public string? MailPassword { get; set; }

        /// <summary>
        /// Up to 10000 characters. It is not checked to be a URL.
        /// </summary>
        [MaxLength(10000, ErrorMessage = "Must be 10000 characters or fewer")]
        public string? EmailConfirmationRedirectUrl { get; set; }
    }
}
