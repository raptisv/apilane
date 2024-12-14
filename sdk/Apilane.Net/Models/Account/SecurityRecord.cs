using System.Collections.Generic;

namespace Apilane.Net.Models.Account
{
    public class SecurityRecord
    {
        public string Role { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Action { get; set; } = null!;
        public List<string> Properties { get; set; } = null!;
    }
}
