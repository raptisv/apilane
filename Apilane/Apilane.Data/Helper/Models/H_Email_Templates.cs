namespace Apilane.Data.Helper.Models
{
    public class H_Email_Templates
    {
        public long ID { get; set; }
        public string Description { get; set; } = null!;
        public bool Active { get; set; }
        public string EventCode { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string Content { get; set; } = null!;
    }
}
