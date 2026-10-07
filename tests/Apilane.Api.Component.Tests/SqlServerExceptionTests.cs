using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Filters;
using Apilane.Api.Models.ViewModels;
using Apilane.Api.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Exercises the real API exception filter with driver errors, without a host or database.
    /// Privileged management callers must not receive the stored values in SQL error messages.
    /// </summary>
    public class SqlServerExceptionTests
    {
        private const string Secret = "private-record-value@example.test";
        private const string IndexFailure = "The CREATE UNIQUE INDEX statement terminated because a duplicate key was found for the object name 'dbo.Orders' and the index name 'UNIQUE_Orders_Email'. The duplicate key value is (" + Secret + ").";
        private const string DuplicateDataMessage = "Cannot create a unique constraint because duplicate values exist. Remove duplicate values and try again.";

        [Theory]
        [InlineData(HostingEnvironment.Production, false)]
        [InlineData(HostingEnvironment.Production, true)]
        [InlineData(HostingEnvironment.Development, false)]
        [InlineData(HostingEnvironment.Development, true)]
        public async Task Unique_Index_Failure_Should_Hide_Record_Values_For_Every_Caller(HostingEnvironment environment, bool privileged)
        {
            var result = await FilterAsync(CreateSqlException((1505, IndexFailure)), environment, privileged);

            Assert.Equal(nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), result.Code);
            Assert.Equal(DuplicateDataMessage, result.Message);
            Assert.Equal(string.Empty, result.Property);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Theory]
        [InlineData(1750, 1505)]
        [InlineData(3621, 1505)]
        [InlineData(2627, 1505)]
        [InlineData(1505, 1750)]
        public async Task Unique_Index_Failure_In_A_Batch_Should_Not_Depend_On_The_First_Error(int first, int second)
        {
            var exception = CreateSqlException((first, first == 1505 ? IndexFailure : "Another SQL error: " + Secret),
                (second, second == 1505 ? IndexFailure : "Another SQL error: " + Secret));
            Assert.Equal(first, exception.Number);
            Assert.Contains(Secret, exception.Message);

            var result = await FilterAsync(exception, HostingEnvironment.Production, privileged: true);

            Assert.Equal(nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), result.Code);
            Assert.Equal(DuplicateDataMessage, result.Message);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Theory]
        [InlineData(2601)]
        [InlineData(2627)]
        public async Task Duplicate_Insert_In_A_Batch_Should_Keep_The_Safe_Unique_Error(int number)
        {
            var message = number == 2627
                ? "Violation of UNIQUE KEY constraint 'UNIQUE_Orders_Email'. Cannot insert duplicate key in object 'dbo.Orders'. The duplicate key value is (" + Secret + ")."
                : "Cannot insert duplicate key row in object 'dbo.Orders' with unique index 'UNIQUE_Orders_Email'. The duplicate key value is (" + Secret + ").";
            var exception = CreateSqlException((3621, "The statement has been terminated. " + Secret), (number, message));

            var result = await FilterAsync(exception, HostingEnvironment.Production, privileged: true);

            Assert.Equal(nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), result.Code);
            Assert.Equal("Value already exists", result.Message);
            Assert.Equal(number == 2627 ? "Email" : string.Empty, result.Property);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Theory]
        [InlineData(1505, nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), DuplicateDataMessage)]
        [InlineData(2601, nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), "Value already exists")]
        [InlineData(2627, nameof(AppErrors.UNIQUE_CONSTRAINT_VIOLATION), "Value already exists")]
        [InlineData(515, nameof(AppErrors.REQUIRED), "Required")]
        public async Task Known_Error_With_Unrecognized_Text_Should_Keep_A_Safe_Numeric_Mapping(int number, string code, string message)
        {
            var result = await FilterAsync(CreateSqlException((number, "Localized database error: " + Secret)),
                HostingEnvironment.Development, privileged: true);

            Assert.Equal(code, result.Code);
            Assert.Equal(message, result.Message);
            Assert.Equal(string.Empty, result.Property);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Theory]
        [InlineData(HostingEnvironment.Production, 50000)]
        [InlineData(HostingEnvironment.Development, 50000)]
        [InlineData(HostingEnvironment.Production, 547)]
        [InlineData(HostingEnvironment.Development, 547)]
        public async Task Unknown_Or_Unrecognized_Sql_Error_Should_Not_Restore_Raw_Text_For_Privileged_Callers(HostingEnvironment environment, int number)
        {
            var result = await FilterAsync(CreateSqlException((number, "Database diagnostic: " + Secret)), environment, privileged: true);

            Assert.Equal(nameof(AppErrors.ERROR), result.Code);
            Assert.Equal(Globals.GeneralError, result.Message);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Theory]
        [InlineData(515, "Cannot insert the value NULL into column 'CustomerId', table 'database.dbo.Orders'; column does not allow nulls. INSERT fails.", nameof(AppErrors.REQUIRED), "Required", "CustomerId")]
        [InlineData(547, "The INSERT statement conflicted with the FOREIGN KEY constraint \"FOREIGN_KEY_Orders_CustomerId_Users\".", nameof(AppErrors.FOREIGN_KEY_CONSTRAINT_VIOLATION), "Foreign key constraint violation", "")]
        public async Task Required_And_Foreign_Key_Errors_Should_Preserve_Their_Safe_Details(int number, string message, string code, string expectedMessage, string property)
        {
            var result = await FilterAsync(CreateSqlException((number, message), (3621, "The statement has been terminated. " + Secret)),
                HostingEnvironment.Production, privileged: true);

            Assert.Equal(code, result.Code);
            Assert.Equal(expectedMessage, result.Message);
            Assert.Equal(property, result.Property);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        }

        [Fact]
        public async Task Domain_Validation_Should_Preserve_Its_Public_Message_And_Property()
        {
            var result = await FilterAsync(new ApilaneException(AppErrors.VALIDATION, "Choose a valid email.", "Email", "Orders"),
                HostingEnvironment.Production, privileged: true);

            Assert.Equal(nameof(AppErrors.VALIDATION), result.Code);
            Assert.Equal("Choose a valid email.", result.Message);
            Assert.Equal("Email", result.Property);
            Assert.Equal("Orders", result.Entity);
        }

        private static async Task<ApiErrorVm> FilterAsync(Exception exception, HostingEnvironment environment, bool privileged)
        {
            var query = A.Fake<IQueryDataService>();
            A.CallTo(() => query.IsPortalRequest).Returns(privileged);
            A.CallTo(() => query.AuthToken).Returns("portal-test-token");
            A.CallTo(() => query.AppToken).Returns("application-test-token");
            A.CallTo(() => query.Entity).Returns("Orders");
            A.CallTo(() => query.RouteController).Returns("Application");
            A.CallTo(() => query.RouteAction).Returns("UpdateEntityConstraints");
            A.CallTo(() => query.IPAddress).Returns("127.0.0.1");
            A.CallTo(() => query.UriParams).Returns(new Dictionary<string, string>());
            var portal = A.Fake<IPortalInfoService>();
            A.CallTo(() => portal.UserOwnsApplicationAsync("portal-test-token", "application-test-token")).Returns(privileged);
            using var services = new ServiceCollection()
                .AddSingleton(query)
                .AddSingleton(portal)
                .BuildServiceProvider();
            var httpContext = new DefaultHttpContext { RequestServices = services };
            var context = new ExceptionContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
            {
                Exception = exception
            };
            var configuration = new ApiConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Environment"] = environment.ToString(),
                ["Url"] = "http://localhost:5001",
                ["PortalUrl"] = "http://portal.test",
                ["FilesPath"] = System.IO.Path.GetTempPath(),
                ["InstallationKey"] = "test-installation-key"
            }).Build());

            await new ApiExceptionFilter(NullLogger<ApiExceptionFilter>.Instance, configuration).OnExceptionAsync(context);

            Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
            return Assert.IsType<ApiErrorVm>(Assert.IsType<JsonResult>(context.Result).Value);
        }

        private static SqlException CreateSqlException(params (int Number, string Message)[] messages)
        {
            // SqlClient exposes no public constructors for server errors. Build the real driver
            // collection so the test covers Number versus Errors and the combined Message.
            var errors = (SqlErrorCollection)(Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)
                ?? throw new InvalidOperationException("Could not create a SQL error collection."));
            var add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The SQL error collection has no Add method.");
            var constructor = typeof(SqlError).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception) }, modifiers: null)
                ?? throw new InvalidOperationException("The SQL error constructor was not found.");
            foreach (var (number, message) in messages)
            {
                var error = constructor.Invoke(new object?[] { number, (byte)1, (byte)16, "test-server", message, "", 1, null });
                add.Invoke(errors, new[] { error });
            }

            var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
                new[] { typeof(SqlErrorCollection), typeof(string) }, modifiers: null)
                ?? throw new InvalidOperationException("The SQL exception factory was not found.");
            return (SqlException)(create.Invoke(null, new object[] { errors, "16.0" })
                ?? throw new InvalidOperationException("Could not create a SQL exception."));
        }
    }
}
