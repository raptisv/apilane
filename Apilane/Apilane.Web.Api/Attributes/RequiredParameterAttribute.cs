using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;
using System.Linq;

namespace Apilane.Web.Api.Attributes
{
    public class RequiredParameterAttribute : ActionMethodSelectorAttribute
    {
        public RequiredParameterAttribute(string valueName)
        {
            ValueName = valueName;
        }

        public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
        {
            return routeContext.HttpContext.Request.Query.Keys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.ToLower().Trim())
                .Contains((ValueName ?? string.Empty).ToLower().Trim());
        }

        public string ValueName { get; private set; }
    }
}
