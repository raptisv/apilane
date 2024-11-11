﻿using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;

namespace Apilane.Web.Portal.Controllers
{
    public class BaseWebApplicationEntityPropertyController : BaseWebApplicationEntityController
    {
        protected DBWS_EntityProperty? EntityProperty = null!;

        public BaseWebApplicationEntityPropertyController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            var propertyName = Utils.GetString(RouteData.Values["propid"]);

            EntityProperty = Entity.Properties.SingleOrDefault(x => x.Name.Equals(propertyName));

            ViewBag.Property = EntityProperty;
        }
    }
}