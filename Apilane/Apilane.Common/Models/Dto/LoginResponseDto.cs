using System.Collections.Generic;

namespace Apilane.Common.Models.Dto
{
    public class LoginResponseDto
    {
        public Dictionary<string, object?> User { get; set; } = null!;
        public string AuthToken { get; set; } = null!;
    }
}
