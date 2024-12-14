namespace Apilane.Common.Helpers
{
    public class EmailSettings
    {
        public string MailServer { get; set; } = null!;
        public int MailServerPort { get; set; }
        public string MailFromAddress { get; set; } = null!;
        public string MailFromDisplayName { get; set; } = null!;
        public string MailUserName { get; set; } = null!;
        public string MailPassword { get; set; } = null!;
    }
}
