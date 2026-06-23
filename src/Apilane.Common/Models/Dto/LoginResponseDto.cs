using System.Collections.Generic;

namespace Apilane.Common.Models.Dto
{
    public class LoginResponseDto
    {
        public Dictionary<string, object?> User { get; set; } = null!;
        public string AuthToken { get; set; } = null!;

        /// <summary>
        /// The AuthTokens.ID of the issued token. Used as the public key id for the
        /// signed-request authentication scheme (where the AuthToken itself is never sent).
        /// </summary>
        public long AuthTokenID { get; set; }
    }
}
