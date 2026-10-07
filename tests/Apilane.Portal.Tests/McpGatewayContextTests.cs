using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using FakeItEasy;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Tests
{
    public class McpGatewayContextTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CallAsync_Should_Preserve_The_Outer_Request_During_And_After_Dispatch(bool fail)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAuthorization();
            services.AddHttpContextAccessor();
            await using var provider = services.BuildServiceProvider();
            var accessor = provider.GetRequiredService<IHttpContextAccessor>();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "delegated-agent"),
                new Claim(McpConnectionScope.ConnectionId, "connection")
            }, "AgentKey"));
            var outer = new DefaultHttpContext { User = principal };
            outer.Request.Path = "/api/mcp";
            accessor.HttpContext = outer;
            using var activity = new Activity("MCP request").Start();
            using var cancellation = new CancellationTokenSource();
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo("el-GR");
            var uiCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Dispatch failed.");

            var endpoint = new RouteEndpoint(async context =>
            {
                Assert.NotSame(outer, context);
                Assert.Same(context, accessor.HttpContext);
                Assert.Same(principal, context.User);
                Assert.Equal(cancellation.Token, context.RequestAborted);
                Assert.Same(activity, Activity.Current);
                Assert.Equal(culture, CultureInfo.CurrentCulture);
                Assert.Equal(uiCulture, CultureInfo.CurrentUICulture);
                entered.SetResult();
                await resume.Task.WaitAsync(context.RequestAborted);
                Assert.Same(context, accessor.HttpContext);
                Assert.Same(principal.Identity, accessor.HttpContext?.User.Identity);
                if (fail)
                {
                    throw failure;
                }
                await context.Response.WriteAsync("{\"ok\":true}", context.RequestAborted);
            }, RoutePatternFactory.Parse("api/v1/context-test"), 0,
                new EndpointMetadataCollection(new ControllerActionDescriptor
                {
                    ControllerName = "ContextTest", ActionName = "Invoke"
                }, new HttpMethodMetadata(new[] { "GET" }), new AuthorizeAttribute()), "Context test");
            var host = A.Fake<IMcpPortalStore>();
            A.CallTo(() => host.AgentAuthenticationType).Returns("AgentKey");
            var policy = A.Fake<IMcpOperationPolicy>();
            A.CallTo(() => policy.IsAvailable(A<ControllerActionDescriptor>._, endpoint, "GET")).Returns(true);
            var gateway = new PortalMcpOperationService(new DefaultEndpointDataSource(endpoint),
                provider.GetRequiredService<IServiceScopeFactory>(), accessor, A.Fake<ISwaggerProvider>(), host, policy);

            try
            {
                var dispatch = gateway.CallAsync(principal, new Dictionary<string, JsonElement>
                {
                    ["operationId"] = JsonSerializer.SerializeToElement("ContextTest_Invoke")
                }, cancellation.Token);
                // The child is still active when the caller observes its ambient request.
                await Task.WhenAny(entered.Task, dispatch);
                if (!entered.Task.IsCompleted)
                {
                    await dispatch;
                }
                var duringDispatch = accessor.HttpContext;
                resume.SetResult();
                if (fail)
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch));
                }
                else
                {
                    var result = await dispatch;
                    Assert.False(result.IsError);
                    Assert.True(result.Result.GetProperty("data").GetProperty("ok").GetBoolean());
                }
                Assert.Same(outer, duringDispatch);
                Assert.Same(outer, accessor.HttpContext);
                Assert.Same(principal.Identity, accessor.HttpContext?.User.Identity);
                Assert.Equal("/api/mcp", outer.Request.Path.Value);
                Assert.Same(activity, Activity.Current);
                Assert.Equal(culture, CultureInfo.CurrentCulture);
                Assert.Equal(uiCulture, CultureInfo.CurrentUICulture);
            }
            finally
            {
                resume.TrySetResult();
                accessor.HttpContext = null;
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }
    }
}
