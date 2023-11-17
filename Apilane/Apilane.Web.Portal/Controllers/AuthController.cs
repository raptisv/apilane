using Microsoft.AspNetCore.Mvc;

namespace Apilane.Web.Portal.Controllers
{
    public class AuthenticateController : Controller
    {
        /// <summary>
        /// Used from external apps to authenticate the user is Apilane Admin.
        /// </summary>
        public IActionResult InRole([FromQuery]string? role)
        {
            if (string.IsNullOrWhiteSpace(role) || 
                !(User?.IsInRole(role) ?? false))
            {
                return Unauthorized();
            }
            
            return Ok();
        }
    }
}