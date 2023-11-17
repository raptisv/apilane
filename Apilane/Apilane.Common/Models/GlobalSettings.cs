using Apilane.Common.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Models
{

    public class GlobalSettings
    {
        
        public long ID { get; set; }

        [AttrRequired]
        
        [Display(Name = "Title"), MinLength(3), MaxLength(16)]
        public string InstanceTitle { get; set; } = null!;

        [AttrRequired]
        
        [Display(Name = "Installation key"), MinLength(30), MaxLength(100)]
        [RegularExpression(@"[^\s]+", ErrorMessage = "Not a valid guid")]
        public string InstallationKey { get; set; } = null!;

        [AttrRequired]
        
        [Display(Name = "Allow register to portal")]
        public bool AllowRegisterToPortal { get; set; }

        
        public string? MailServer { get; set; }

        
        public int? MailServerPort { get; set; }

        
        public string? MailFromAddress { get; set; }

        
        public string? MailFromDisplayName { get; set; }

        
        public string? MailUserName { get; set; }

        
        public string? MailPassword { get; set; }

        public bool IsMailSetup()
        {
            return !string.IsNullOrWhiteSpace(MailServer) &&
                !string.IsNullOrWhiteSpace(MailFromAddress) &&
                !string.IsNullOrWhiteSpace(MailFromDisplayName) &&
                !string.IsNullOrWhiteSpace(MailUserName) &&
                !string.IsNullOrWhiteSpace(MailPassword) &&
                MailServerPort.HasValue;
        }
    }
}
