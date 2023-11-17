using Apilane.Common.Attributes;
using Apilane.Common.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class DBWS_Application : DBWS_MainModel
    {
        public string Token { get; set; } = null!;
        public string UserID { get; set; } = null!;
        public string? AdminEmail { get; set; }

        [AttrRequired]
        [Display(Name = "Name")]
        [MinLength(4), MaxLength(100)]
        public string Name { get; set; } = null!;

        public long ServerID { get; set; }

        [AttrRequired]
        [Display(Name = "Server")]
        public DBWS_Server Server { get; set; } = null!;

        [AttrRequired]
        [Display(Name = "Encryption key")]

        public string EncryptionKey { get; set; } = null!;

        [Display(Name = "Online")]

        public bool Online { get; set; }

        [Display(Name = "Force only one login at a time")]

        public bool ForceSingleLogin { get; set; }

        [AttrRequired]
        [Range(1, int.MaxValue, ErrorMessage = "The value must be between 1 and 2147483647")]
        [Display(Name = "Authentication token expiration")]

        public int AuthTokenExpireMinutes { get; set; }

        [MaxLength(10000)]
        [Display(Name = "Email confirmation redirect url")]

        public string? EmailConfirmationRedirectUrl { get; set; }

        [Range(1, 25600, ErrorMessage = "The value must be between 1 and 25600 (1KB - 25MB)")]
        [Display(Name = "Maximum allowed file size (KB)")]

        public int MaxAllowedFileSizeInKB { get; set; }

        [AttrRequired]
        [Display(Name = "Database type")]
        public int DatabaseType { get; set; }

        [Display(Name = "Allow users with unconfirmed email to login")]
        public bool AllowLoginUnconfirmedEmail { get; set; }

        [Display(Name = "Allow new users to register")]
        public bool AllowUserRegister { get; set; }

        [Display(Name = "Connection string")]
        public string? ConnectionString { get; set; }

        [Display(Name = "Mail server")]
        public string? MailServer { get; set; }

        [Range(1, 65535, ErrorMessage = "The value must be between 1 and 65535")]
        [Display(Name = "Mail server port")]

        public int? MailServerPort { get; set; }

        [Display(Name = "Mail from address")]
        public string? MailFromAddress { get; set; }

        [Display(Name = "Mail from display name")]
        public string? MailFromDisplayName { get; set; }

        [Display(Name = "Mail user name")]
        public string? MailUserName { get; set; }

        [Display(Name = "Mail password")]
        public string? MailPassword { get; set; }


        public int ClientIPsLogic { get; set; }


        public string? ClientIPsValue { get; set; }

        /// <summary>
        /// If this value is set, every entity can have one extra system property with that name.
        /// Every api call on that application, appends a filter depending on the user's value on that property
        /// </summary>

        [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Allowed chatracters are a-z, A-Z")]
        [MaxLength(40)]
        public string? DifferentiationEntity { get; set; }

        [Display(Name = "Security")]
        public string? Security { get; set; } = null!;

        [JsonIgnore]
        public List<DBWS_Security> Security_List
        {
            get
            {
                return string.IsNullOrWhiteSpace(Security)
                    ? new List<DBWS_Security>()
                    : JsonSerializer.Deserialize<List<DBWS_Security>>(Security!) ?? throw new Exception($"Could not deserialize Security {Security}");
            }
        }

        public List<DBWS_Entity> Entities { get; set; } = null!;


        public List<DBWS_ReportItem> Reports { get; set; } = null!;


        public List<DBWS_CustomEndpoint> CustomEndpoints { get; set; } = null!;


        public List<DBWS_Collaborate> Collaborates { get; set; } = null!;

        public EmailSettings? GetEmailSettings()
        {
            if (string.IsNullOrWhiteSpace(MailServer) ||
                !MailServerPort.HasValue ||
                MailServerPort == 0 ||
                string.IsNullOrWhiteSpace(MailFromAddress) ||
                string.IsNullOrWhiteSpace(MailFromDisplayName) ||
                string.IsNullOrWhiteSpace(MailUserName) ||
                string.IsNullOrWhiteSpace(MailPassword))
            {
                return null;
            }

            return new EmailSettings()
            {
                MailServerPort = this.MailServerPort.Value,
                MailFromAddress = this.MailFromAddress,
                MailFromDisplayName = this.MailFromDisplayName,
                MailPassword = this.MailPassword,
                MailServer = this.MailServer,
                MailUserName = this.MailUserName
            };
        }
    }
}
