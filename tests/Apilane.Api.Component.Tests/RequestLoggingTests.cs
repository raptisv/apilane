using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Extensions;
using Apilane.Api.Filters;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The filters that log every request put the query string of the request in the log scope, and a sink
    /// (Graylog) stores the scope as properties of the entry. The links in a confirmation or a password
    /// reset mail, and a file download link, carry a token in the query string: it must not reach the log.
    /// These tests need neither a host nor a database.
    /// </summary>
    public class RequestLoggingTests
    {
        private static ActionContext CreateRequest(string controller, string action, string query)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString(query);

            var routeData = new RouteData();
            routeData.Values["controller"] = controller;
            routeData.Values["action"] = action;

            var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

            httpContext.RequestServices = new ServiceCollection()
                .AddSingleton<IQueryDataService>(new HttpQueryDataService(new ActionContextAccessor() { ActionContext = actionContext }))
                .BuildServiceProvider();

            return actionContext;
        }

        private static async Task<List<CapturedLogEntry>> RunLogActionFilterAsync(string controller, string action, string query)
        {
            var actionContext = CreateRequest(controller, action, query);
            var logger = new CapturingLogger<ApplicationLogActionFilter>();

            var executingContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

            await new ApplicationLogActionFilter(logger).OnActionExecutionAsync(
                executingContext,
                () => Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())));

            return logger.Entries;
        }

        private static async Task<List<CapturedLogEntry>> RunExceptionFilterAsync(string controller, string action, string query)
        {
            var actionContext = CreateRequest(controller, action, query);
            var logger = new CapturingLogger<ApiExceptionFilter>();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://portal.test",
                    ["FilesPath"] = System.IO.Path.GetTempPath(),
                    ["InstallationKey"] = "test-installation-key"
                })
                .Build();

            // An exception that is not the caller's doing: the filter logs it with the request's parameters
            var exceptionContext = new ExceptionContext(actionContext, new List<IFilterMetadata>())
            {
                Exception = new InvalidOperationException("Something broke")
            };

            await new ApiExceptionFilter(logger, new ApiConfiguration(configuration)).OnExceptionAsync(exceptionContext);

            return logger.Entries;
        }

        [Fact]
        public async Task LogActionFilter_RequestWithParameters_LogsTheParameters()
        {
            var appToken = Guid.NewGuid().ToString();

            var entries = await RunLogActionFilterAsync("Data", "Get", $"?appToken={appToken}&entity=Orders&pageSize=5");

            var entry = Assert.Single(entries);
            Assert.Contains("entity", entry.Text);
            Assert.Contains("Orders", entry.Text);
            Assert.Contains("pageSize", entry.Text);
        }

        [Fact]
        public async Task ExceptionFilter_RequestWithParameters_LogsTheParameters()
        {
            var appToken = Guid.NewGuid().ToString();

            var entries = await RunExceptionFilterAsync("Data", "Get", $"?appToken={appToken}&entity=Orders&pageSize=5");

            var entry = Assert.Single(entries);
            Assert.Contains("entity", entry.Text);
            Assert.Contains("Orders", entry.Text);
            Assert.Contains("pageSize", entry.Text);
        }

        [Fact]
        public void GetUriParamsForLog_SecretInTheQueryString_LeavesTheRealParametersAlone()
        {
            // A custom endpoint gets the parameters of the request as they are: only the copy for the log is masked
            var secret = Guid.NewGuid().ToString();
            var queryService = CreateRequest("Custom", "Get", $"?appToken={Guid.NewGuid()}&password={secret}&entity=Orders")
                .HttpContext.RequestServices.GetRequiredService<IQueryDataService>();

            var forLog = queryService.GetUriParamsForLog();

            Assert.Equal(secret, queryService.UriParams["password"]);
            Assert.DoesNotContain(secret, forLog["password"]);
            Assert.Equal("Orders", forLog["entity"]);
        }

        [Theory]
        [InlineData("appToken")]
        [InlineData("AppToken")]
        public void GetUriParamsForLog_ApplicationToken_IsNotMasked(string parameter)
        {
            // It contains 'token' but it is the public identifier of the application, in every URL: masking it
            // would leave the entries of the log without the application they belong to
            var appToken = Guid.NewGuid().ToString();
            var queryService = CreateRequest("Data", "Get", $"?{parameter}={appToken}&entity=Orders")
                .HttpContext.RequestServices.GetRequiredService<IQueryDataService>();

            var forLog = queryService.GetUriParamsForLog();

            Assert.Equal(appToken, forLog[parameter]);
        }

        [Theory]
        [InlineData("Account", "Confirm", "token")]
        [InlineData("Account", "Confirm", "Token")]
        [InlineData("Files", "Download", "authToken")]
        [InlineData("Custom", "Get", "password")]
        [InlineData("Custom", "Get", "clientSecret")]
        [InlineData("Files", "Download", "signature")]
        public async Task LogActionFilter_SecretInTheQueryString_DoesNotLogTheValue(string controller, string action, string parameter)
        {
            var secret = Guid.NewGuid().ToString();

            var entries = await RunLogActionFilterAsync(controller, action, $"?appToken={Guid.NewGuid()}&{parameter}={secret}");

            Assert.NotEmpty(entries);
            Assert.All(entries, entry => Assert.DoesNotContain(secret, entry.Text));
        }

        [Theory]
        [InlineData("Account", "Confirm", "token")]
        [InlineData("Account", "Confirm", "Token")]
        [InlineData("Files", "Download", "authToken")]
        [InlineData("Custom", "Get", "password")]
        [InlineData("Custom", "Get", "clientSecret")]
        [InlineData("Files", "Download", "signature")]
        public async Task ExceptionFilter_SecretInTheQueryString_DoesNotLogTheValue(string controller, string action, string parameter)
        {
            var secret = Guid.NewGuid().ToString();

            var entries = await RunExceptionFilterAsync(controller, action, $"?appToken={Guid.NewGuid()}&{parameter}={secret}");

            Assert.NotEmpty(entries);
            Assert.All(entries, entry => Assert.DoesNotContain(secret, entry.Text));
        }
    }
}
