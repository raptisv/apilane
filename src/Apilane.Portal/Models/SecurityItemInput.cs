namespace Apilane.Portal.Models
{
    /// <summary>
    /// Input DTO for security items submitted as JSON from the Security form.
    /// </summary>
    public class SecurityItemInput
    {
        public string Name { get; set; } = null!;
        public int TypeID { get; set; }
        public string Action { get; set; } = null!;
        public string RoleID { get; set; } = null!;
        public int Record { get; set; }
        public string? Properties { get; set; }
        public int RateLimitType { get; set; }
        public int RateLimitValue { get; set; }
    }
}
