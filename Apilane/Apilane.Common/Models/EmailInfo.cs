namespace Apilane.Common.Models
{
    public class EmailInfo
    {
        public string MailServer { get; set; } = null!;
        public int MailServerPort { get; set; }
        public string MailFromAddress { get; set; } = null!;
        public string MailFromDisplayName { get; set; } = null!;
        public string MailUserName { get; set; } = null!;
        public string MailPassword { get; set; } = null!;
        public string[] Recipients { get; set; } = null!;
        public string[] Recipients_CC { get; set; } = null!;
        public string[] Recipients_BCC { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string Body { get; set; } = null!;
    }
}
