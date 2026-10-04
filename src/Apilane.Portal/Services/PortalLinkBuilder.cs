using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Http;
using System;

namespace Apilane.Portal.Services
{
    public class PortalLinkBuilder : IPortalLinkBuilder
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PortalLinkBuilder(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string Ui(string path)
        {
            var request = _httpContextAccessor.HttpContext?.Request
                ?? throw new Exception("A portal link can only be built during a request.");

            return $"{request.Scheme}://{request.Host}{request.PathBase}/{path.TrimStart('/')}";
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
