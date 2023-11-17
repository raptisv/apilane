namespace Apilane.Common.Models.Dto
{
    public class UserSecurityDto
    {
        public string Role { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Action { get; set; } = null!;
        public string[] Properties { get; set; } = null!;
    }
}
