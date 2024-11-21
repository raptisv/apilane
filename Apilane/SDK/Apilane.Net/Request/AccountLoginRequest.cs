﻿namespace Apilane.Net.Request
{
    public class AccountLoginRequest : ApilaneRequestBase<AccountLoginRequest>
    {
        public static AccountLoginRequest New() => new();

        private AccountLoginRequest() : base(null, "Account", "Login")
        {

        }
    }
}
