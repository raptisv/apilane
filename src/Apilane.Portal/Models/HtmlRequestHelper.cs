using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace Apilane.Portal.Models
{
    public static class HtmlRequestHelper
    {
        public static bool IsDarkTheme(this IHtmlHelper htmlHelper, HttpRequest request)
        {
            return request.Cookies.ContainsKey("theme")
                ? (request.Cookies["theme"] ?? string.Empty).ToString().Trim().Equals("dark", StringComparison.OrdinalIgnoreCase)
                : true;
        }

        public static string AssemblyVersion(this IHtmlHelper _)
        {
            return Versioning.GetVersion(Assembly.GetEntryAssembly()!);
        }

        public static string Controller(this IHtmlHelper htmlHelper)
        {
            var routeValues = htmlHelper.ViewContext.HttpContext.Request.RouteValues;

            if (routeValues.ContainsKey("controller"))
                return ((string)routeValues["controller"]!).ToLower();

            return string.Empty;
        }

        public static string Action(this IHtmlHelper htmlHelper)
        {
            var routeValues = htmlHelper.ViewContext.HttpContext.Request.RouteValues;

            if (routeValues.ContainsKey("action"))
                return ((string)routeValues["action"]!).ToLower();

            return string.Empty;
        }

        public static string GetEntName(this IHtmlHelper htmlHelper)
        {
            var routeValues = htmlHelper.ViewContext.HttpContext.Request.RouteValues;

            if (routeValues.ContainsKey("entid"))
                return Utils.GetString(routeValues["entid"]);

            return string.Empty;
        }

        public static string GetPropName(this IHtmlHelper htmlHelper)
        {
            var routeValues = htmlHelper.ViewContext.HttpContext.Request.RouteValues;

            if (routeValues.ContainsKey("propid"))
                return Utils.GetString(routeValues["propid"]);

            return string.Empty;
        }

        public static string GetAppToken(this IHtmlHelper htmlHelper)
        {
            var routeValues = htmlHelper.ViewContext.HttpContext.Request.RouteValues;

            if (routeValues.ContainsKey("appid"))
                return Utils.GetString(routeValues["appid"]);

            return string.Empty;
        }

        public static string GetPortalUserAuthToken(this IHtmlHelper htmlHelper, IIdentity identity)
        {
            return identity.GetPortalUserAuthToken();
        }

        public static string OnControllerString(this IHtmlHelper htmlHelper,
            string controller,
            string onItStr,
            string notOnItStr)
        {
            return htmlHelper.Controller().ToLower().Equals(controller.ToLower())
                ? onItStr
                : notOnItStr;
        }

        public static string OnControllerActionString(this IHtmlHelper htmlHelper,
            string controller,
            string action,
            string onItStr,
            string notOnItStr)
        {
            return htmlHelper.Controller().ToLower().Equals(controller.ToLower()) && htmlHelper.Action().ToLower().Equals(action.ToLower())
                ? onItStr
                : notOnItStr;
        }

        public static object GetPropertiesForChart(this IHtmlHelper helper, DBWS_Entity entity, string props)
        {
            var properties = props.Split(',')
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Split('.').Length == 2)
                .Select(x => (entity.Properties.Single(p => p.Name.Equals(x.Split('.')[0], StringComparison.InvariantCultureIgnoreCase)), $"{x.Split('.')[0]}.{x.Split('.')[1].ToLower()}"))
                .ToList();

            return properties.Select(x => new
            {
                Raw = x.Item2,
                x.Item1.Name
            });
        }

        public static object GetGroupsForChart(this IHtmlHelper helper, DBWS_Entity entity, string groups)
        {
            var groupsss = groups.Split(',')
                .Where(x => !string.IsNullOrWhiteSpace(x) && entity.Properties.Any(p => p.Name.Equals(x.Split('.')[0], StringComparison.InvariantCultureIgnoreCase)))
                .Select(x => new Tuple<DBWS_EntityProperty, string>(entity.Properties.First(p => p.Name.Equals(x.Split('.')[0], StringComparison.InvariantCultureIgnoreCase)), x))
                .ToList();

            return groupsss.Select(x => new
                                    {
                                        Raw = x.Item2,
                                        x.Item1.Name,
                                        Type = x.Item1.TypeID_Enum.ToString(),
                                        Sub = x.Item2.Split('.').Last().ToLower()
                                    })
                                    .GroupBy(x => x.Type).Select(x => new
                                    {
                                        Type = x.Key,
                                        Values = x.GroupBy(g => g.Name)
                                        .Select(g => new { Name = g.Key, Values = g.ToList() }).ToList()
                                    });
        }

        private const string ScriptsKey = "DelayedScripts";

        public static IDisposable BeginScripts(this IHtmlHelper helper)
        {
            return new ScriptBlock(helper.ViewContext);
        }

        public static HtmlString PageScripts(this IHtmlHelper helper)
        {
            return new HtmlString(string.Join(Environment.NewLine, GetPageScriptsList(helper.ViewContext.HttpContext)));
        }

        private static List<string> GetPageScriptsList(HttpContext httpContext)
        {
            var pageScripts = (List<string>)httpContext.Items[ScriptsKey]!;
            if (pageScripts == null)
            {
                pageScripts = new List<string>();
                httpContext.Items[ScriptsKey] = pageScripts;
            }
            return pageScripts;
        }

        private class ScriptBlock : IDisposable
        {
            private readonly TextWriter _originalWriter;
            private readonly StringWriter _scriptsWriter;

            private readonly ViewContext _viewContext;

            public ScriptBlock(ViewContext viewContext)
            {
                _viewContext = viewContext;
                _originalWriter = _viewContext.Writer;
                _viewContext.Writer = _scriptsWriter = new StringWriter();
            }

            public void Dispose()
            {
                _viewContext.Writer = _originalWriter;
                var pageScripts = GetPageScriptsList(_viewContext.HttpContext);
                pageScripts.Add(_scriptsWriter.ToString());
            }
        }
    }
}