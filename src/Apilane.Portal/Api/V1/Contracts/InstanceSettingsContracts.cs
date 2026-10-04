using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The settings of this Apilane instance. The mail password and the installation key are never
    /// returned; HasMailPassword and HasInstallationKey say whether they are set.
    /// </summary>
    public class InstanceSettingsResponse
    {
        /// <summary>
        /// The name of this instance. Shown inside the Portal only.
        /// </summary>
        [Required]
        public string InstanceTitle { get; set; } = string.Empty;

        /// <summary>
        /// Whether new users may register on this instance.
        /// </summary>
        [Required]
        public bool AllowRegisterToPortal { get; set; }

        [Required]
        public bool HasInstallationKey { get; set; }

        public string? MailServer { get; set; }

        public int? MailServerPort { get; set; }

        public string? MailUserName { get; set; }

        [Required]
        public bool HasMailPassword { get; set; }

        public string? MailFromAddress { get; set; }

        public string? MailFromDisplayName { get; set; }

        /// <summary>
        /// Whether every mail setting is filled in, so the instance can send password-reset mails.
        /// </summary>
        [Required]
        public bool IsMailSetup { get; set; }
    }

    /// <summary>
    /// New values for the instance settings. Every value is written, except the two secrets:
    /// a null (or left out) InstallationKey or MailPassword keeps the stored value.
    /// </summary>
    public class InstanceSettingsRequest
    {
        private const string TitleLengthMessage = "Must be 3 to 16 characters";
        private const string KeyLengthMessage = "Must be 30 to 100 characters";

        /// <summary>
        /// 3 to 16 characters.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [MinLength(3, ErrorMessage = TitleLengthMessage)]
        [MaxLength(16, ErrorMessage = TitleLengthMessage)]
        public string InstanceTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        public bool? AllowRegisterToPortal { get; set; }

        /// <summary>
        /// The key the API servers use to authenticate to the Portal: 30 to 100 characters without
        /// whitespace. Null keeps the stored key; it cannot be cleared. After a change the API
        /// servers must be restarted with the new key.
        /// </summary>
        [MinLength(30, ErrorMessage = KeyLengthMessage)]
        [MaxLength(100, ErrorMessage = KeyLengthMessage)]
        [RegularExpression(@"[^\s]+", ErrorMessage = "Must not contain whitespace")]
        public string? InstallationKey { get; set; }

        /// <summary>
        /// Null or empty clears the value. The same goes for the other mail settings, except MailPassword.
        /// </summary>
        public string? MailServer { get; set; }

        public int? MailServerPort { get; set; }

        public string? MailUserName { get; set; }

        /// <summary>
        /// Null keeps the stored password, an empty string clears it.
        /// </summary>
        public string? MailPassword { get; set; }

        public string? MailFromAddress { get; set; }

        public string? MailFromDisplayName { get; set; }
    }
}
