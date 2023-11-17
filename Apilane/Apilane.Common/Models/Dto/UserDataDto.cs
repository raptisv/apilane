using System.Collections.Generic;

namespace Apilane.Common.Models.Dto
{
    public class UserDataDto
    {
        public Dictionary<string, object?> User { get; set; } = null!;
        public List<UserSecurityDto> Security { get; set; } = null!;
    }
}
