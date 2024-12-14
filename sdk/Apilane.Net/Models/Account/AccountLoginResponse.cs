using System.Collections.Generic;

namespace Apilane.Net.Models.Account
{
    public class AccountLoginResponse<T> where T : IApiUser
    {
        public string AuthToken { get; set; } = null!;

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
