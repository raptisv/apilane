namespace Apilane.Common.Models.Dto
{
    public class EmailTemplateDto
    {
        public required int ID { get; set; }
        public required string EventCode { get; set; }
        public required bool Active { get; set; }
        public required string Subject { get; set; }
        public required string Content { get; set; }
    }
}
