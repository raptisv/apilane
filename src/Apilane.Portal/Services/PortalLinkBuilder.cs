using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using System;

namespace Apilane.Portal.Services
{
    public class PortalLinkBuilder : IPortalLinkBuilder
    {
        private readonly PortalConfiguration _configuration;

        public PortalLinkBuilder(PortalConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Ui(string path)
        {
            var origin = _configuration.PublicUrl
                ?? throw PortalException.Conflict("Configure PublicUrl with the Portal's public http or https origin before sending email.");

            return $"{origin.AbsoluteUri}{path.TrimStart('/')}";
        }

        public string ResetPassword(string code)
        {
            return Ui($"account/reset-password?code={Uri.EscapeDataString(code)}");
        }

        public string ForgotPassword()
        {
            return Ui("account/forgot-password");
        }
    }
}
