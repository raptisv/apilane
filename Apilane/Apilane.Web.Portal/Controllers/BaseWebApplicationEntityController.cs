using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;

namespace Apilane.Web.Portal.Controllers
{
    public class BaseWebApplicationEntityController : BaseWebApplicationController
    {
        protected DBWS_Entity Entity = null!;

        public BaseWebApplicationEntityController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            var entityName = Utils.GetString(RouteData.Values["entid"]);

            Entity = Application.Entities.SingleOrDefault(x => x.Name.Equals(entityName))
                ?? throw new Exception($"Entity with name '{entityName}' not found");

            ViewBag.Entity = Entity;
        }
    }
}
