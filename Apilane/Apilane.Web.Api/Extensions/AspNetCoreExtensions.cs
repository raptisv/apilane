using Apilane.Common.Utilities;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Extentions
{
    public class AspNetCoreExtensions
    {
        public static HealthCheckOptions SetupHealthCheck(string tag)
        {
            static Task HealthReportAsync(HttpContext c, HealthReport r)
            {
                c.Response.ContentType = "application/json";
                return c.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    Status = r.Status.ToString(),
                    Application = Assembly.GetEntryAssembly()?.GetName().Name,
                    Version = Assembly.GetEntryAssembly()?.GetVersion(),
                    Source = Environment.MachineName,
                    Entries = r.Entries?.Select(e => new { key = e.Key, value = e.Value.Status.ToString(), details = string.Join(",", e.Value.Data.Select(x => $"{x.Key} = {x.Value}")) })
                }));
            }

            return new HealthCheckOptions()
            {
                Predicate = (check) => { return check.Tags.Contains(tag); },
                ResponseWriter = HealthReportAsync
            };
        }
    }
}
