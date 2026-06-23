using System.Collections.Generic;

namespace Apilane.Net.Models.Account
{
    public class AccountLoginResponse<T> where T : IApiUser
    {
        public string AuthToken { get; set; } = null!;

        /// <summary>
        /// The id of the issued token, used as the public key id for signed-request
        /// authentication (where the AuthToken itself is never transmitted).
        /// </summary>
        public long AuthTokenID { get; set; }

        public T User { get; set; } = default!;
    }

    public class AccountUserDataResponse<T> where T : IApiUser
    {
        public T User { get; set; } = default!;

        public List<SecurityRecord> Security { get; set; } = null!;
    }

    public class AccountRegisterResponse
    {
        public long UserID { get; set; }
    }
}
