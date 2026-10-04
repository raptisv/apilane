using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// What every management API endpoint gets from the shared pipeline: the error body, the CSRF
    /// check, the answer for an unknown route and the cache headers.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApiPipelineTests
    {
        private const string TestUrl = "/api/test/errors";

        private readonly PortalFactory _portal;

        public ApiPipelineTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Errors thrown by an action ----------

        [Fact]
        public async Task Unhandled_Exception_Should_Return_500_With_The_Error_Body()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/exception");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Error, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        }

        [Fact]
        public void Unhandled_Exception_In_Production_Should_Not_Reveal_The_Exception_Message()
        {
            var environment = A.Fake<IWebHostEnvironment>();
            A.CallTo(() => environment.EnvironmentName).Returns("Production");

            var filter = new PortalApiExceptionFilter(NullLogger<PortalApiExceptionFilter>.Instance, environment);

            var context = new ExceptionContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>())
            {
                Exception = new InvalidOperationException("secret detail")
            };

            filter.OnException(context);

            var result = Assert.IsType<ObjectResult>(context.Result);
            var error = Assert.IsType<ErrorResponse>(result.Value);

            Assert.True(context.ExceptionHandled);
            Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
            Assert.Equal(PortalErrorCode.Error, error.Code);
            Assert.Equal("Something went wrong.", error.Message);
        }

        [Fact]
        public async Task PortalException_Should_Return_Its_Status_Code_Entity_And_Property()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/portal-exception");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal("Taken.", error.Message);
            Assert.Equal("Thing", error.Entity);
            Assert.Equal("Name", error.Property);
            Assert.Null(error.Errors);
        }

        [Fact]
        public async Task Conflict_Should_Return_409_CONFLICT()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/conflict");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal("CONFLICT", error.Code);
            Assert.Equal("In use.", error.Message);
            Assert.Equal("Thing", error.Entity);
        }

        [Fact]
        public async Task Forbidden_Should_Return_403_FORBIDDEN()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/forbidden");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("FORBIDDEN", (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Validation_Should_Return_400_VALIDATION_With_Errors()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/validation");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal("VALIDATION", error.Code);
            Assert.NotNull(error.Errors);

            var detail = Assert.Single(error.Errors);
            Assert.Equal("Name", detail.Property);
            Assert.Equal("Already used.", detail.Message);
        }

        [Fact]
        public async Task Status_Only_Result_Should_Return_The_Error_Body_Not_ProblemDetails()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync($"{TestUrl}/bare-not-found");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        }

        // ---------- Errors before the action ----------

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Name\":null}")]
        [InlineData("{\"Name\":")]
        [InlineData("")]
        public async Task Invalid_Body_Should_Return_400_VALIDATION_With_Errors(string body)
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PostAsync($"{TestUrl}/body", new StringContent(body, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.NotNull(error.Errors);
            Assert.NotEmpty(error.Errors);
            Assert.All(error.Errors, x => Assert.False(string.IsNullOrWhiteSpace(x.Message)));
        }

        [Theory]
        // A value of the wrong type: named by the property, not by its JSON path ($.Name).
        [InlineData("{\"Name\":5}", "Name")]
        [InlineData("{\"Name\":", "Name")]
        // A body that is not readable at all.
        [InlineData("{", "$")]
        [InlineData("", "")]
        public async Task Unreadable_Body_Should_Return_One_Error_And_Not_Name_The_Action_Parameter(string body, string property)
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PostAsync($"{TestUrl}/body", new StringContent(body, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal(property, detail.Property);
        }

        [Fact]
        public async Task Missing_Required_Property_Should_Be_Named_In_Errors()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PostAsync($"{TestUrl}/body", new StringContent("{}", Encoding.UTF8, "application/json"));

            var error = await response.ReadJsonAsync<ErrorResponse>();
            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal("Name", detail.Property);
        }

        [Fact]
        public async Task Exception_Outside_The_Action_Should_Return_500_With_The_Error_Body()
        {
            // The session lookup runs in an authorization filter, where MVC exception filters do not reach.
            var access = A.Fake<IPortalAccessService>();
            A.CallTo(() => access.FindCurrentUserAsync()).Throws(new InvalidOperationException("The session lookup failed on purpose."));

            var host = _portal.CreateHost(services =>
            {
                services.RemoveAll<IPortalAccessService>();
                services.AddScoped(_ => access);
            });

            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password, host);

            var response = await session.Client.GetAsync("/api/v1/session");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Error, error.Code);
            // The test host runs as Development, so the detail is shown.
            Assert.Equal("The session lookup failed on purpose.", error.Message);
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        }

        [Fact]
        public async Task Exception_Outside_The_Action_In_Production_Should_Not_Reveal_The_Exception_Message()
        {
            var access = A.Fake<IPortalAccessService>();
            A.CallTo(() => access.FindCurrentUserAsync()).Throws(new InvalidOperationException("The session lookup failed on purpose."));

            var host = _portal.CreateProductionHost(services =>
            {
                services.RemoveAll<IPortalAccessService>();
                services.AddScoped(_ => access);
            });

            var (email, password) = await _portal.CreateUserAsync();
            var session = await _portal.SignInAsync(email, password, host);

            var response = await session.Client.GetAsync("/api/v1/session");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(PortalApiErrors.ErrorMessage, (await response.ReadJsonAsync<ErrorResponse>()).Message);
            Assert.DoesNotContain("failed on purpose", body);
        }

        // ---------- CSRF ----------

        [Theory]
        [InlineData(null)]
        [InlineData("0")]
        [InlineData("true")]
        public async Task Post_Without_The_Csrf_Header_Should_Return_403_Naming_The_Header(string? headerValue)
        {
            var client = await _portal.CreateUserClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            if (headerValue is not null)
            {
                client.DefaultRequestHeaders.Add(PortalCsrfFilter.HeaderName, headerValue);
            }

            var response = await client.PostAsync($"{TestUrl}/body", new { Name = "x" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains("X-Apilane-Portal: 1", error.Message);
        }

        [Fact]
        public async Task Post_With_The_Csrf_Header_Should_Reach_The_Action()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PostAsync($"{TestUrl}/body", new { Name = "x" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Anonymous_Endpoint_Should_Require_The_Csrf_Header_Too()
        {
            var client = _portal.CreateAnonymousClient();

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{TestUrl}/anonymous", null)).StatusCode);

            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PostAsync($"{TestUrl}/anonymous", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Get_Should_Not_Need_The_Csrf_Header()
        {
            var client = await _portal.CreateUserClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.GetAsync("/api/v1/session");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // ---------- Unknown routes ----------

        [Theory]
        [InlineData("GET", "/api/v1/does-not-exist")]
        [InlineData("POST", "/api/v1/does-not-exist")]
        // A known route with the wrong method is reported as unknown, not as 405.
        [InlineData("PUT", "/api/v1/session")]
        [InlineData("DELETE", "/api/v1/admin/servers")]
        public async Task Unknown_Route_Or_Method_Should_Return_404_Json(string method, string url)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(PortalErrorCode.NotFound, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
