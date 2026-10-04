using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// Three new users and one application of the owner, shared with the collaborator, for the
    /// tests of the constraints and the default sorting of an entity. See <see cref="SeedEntities"/>.
    /// </summary>
    public record EntityScene(
        PortalFactory Portal,
        string OwnerEmail,
        HttpClient Owner,
        string CollaboratorEmail,
        HttpClient Collaborator,
        HttpClient Stranger,
        string ServerUrl,
        string Token,
        long AppId)
    {
        public const string OwnerConstraint = "{\"IsSystem\":true,\"TypeID\":2,\"Properties\":\"Owner,Users,ON_DELETE_SET_NULL\"}";

        public string AppUrl => $"/api/v1/applications/{Token}";

        public string ConstraintsUrl(string entity) => $"{AppUrl}/entities/{entity}/constraints";

        public string DefaultOrderUrl(string entity) => $"{AppUrl}/entities/{entity}/default-order";

        public static async Task<EntityScene> CreateAsync(PortalFactory portal, bool ownerIsAdmin = false)
        {
            var (ownerEmail, ownerPassword) = ownerIsAdmin ? await portal.CreateAdminUserAsync() : await portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await portal.CreateUserAsync();

            var server = await portal.CreateServerAsync();
            var seeded = await portal.CreateApplicationAsync(server.ID, ownerEmail, $"entity-scene-{Guid.NewGuid():N}", collaboratorEmail);
            var appId = seeded.Application.ID;

            await portal.WithDbContextAsync(db =>
            {
                db.Entities.AddRange(SeedEntities(appId));

                return db.SaveChangesAsync();
            });

            return new EntityScene(
                portal,
                ownerEmail,
                await portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                server.ServerUrl,
                seeded.Application.Token,
                appId);
        }

        /// <summary>
        /// 'Users' and 'Files' are system entities; 'Users' has a system unique constraint on
        /// 'Email'. 'Orders' has a property of every kind, the system foreign key on 'Owner' and a
        /// custom unique constraint on 'Code'. 'Customers' and 'Invoices' are there to point to.
        /// </summary>
        public static List<DBWS_Entity> SeedEntities(long appId)
        {
            var users = Entity(appId, "Users", true,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Email", PropertyType.String, isSystem: true),
                Property("Nickname", PropertyType.String));

            users.EntConstraints = "[{\"IsSystem\":true,\"TypeID\":1,\"Properties\":\"Email\"}]";

            var files = Entity(appId, "Files", true,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true));

            var customers = Entity(appId, "Customers", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Name", PropertyType.String));

            var orders = Entity(appId, "Orders", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                Property("Owner", PropertyType.Number, isSystem: true, decimalPlaces: 0),
                Property("Created", PropertyType.Date, isSystem: true),
                Property("Customer_ID", PropertyType.Number, decimalPlaces: 0),
                Property("Agent_ID", PropertyType.Number, decimalPlaces: 0),
                Property("Amount", PropertyType.Number, decimalPlaces: 2),
                Property("Code", PropertyType.String),
                Property("Secret", PropertyType.String),
                Property("Paid", PropertyType.Boolean));

            orders.Properties[7].Encrypted = true;
            orders.EntConstraints = $"[{OwnerConstraint},{{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"Code\"}}]";

            var invoices = Entity(appId, "Invoices", false,
                Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true));

            return new List<DBWS_Entity> { users, files, customers, orders, invoices };
        }

        public static DBWS_Entity Entity(long appId, string name, bool isSystem, params DBWS_EntityProperty[] properties)
        {
            return new DBWS_Entity
            {
                AppID = appId,
                Name = name,
                IsSystem = isSystem,
                DateModified = DateTime.UtcNow,
                Properties = properties.ToList()
            };
        }

        public static DBWS_EntityProperty Property(string name, PropertyType type, bool isSystem = false, bool isPrimaryKey = false, int? decimalPlaces = null)
        {
            return new DBWS_EntityProperty
            {
                Name = name,
                TypeID = (int)type,
                IsSystem = isSystem,
                IsPrimaryKey = isPrimaryKey,
                Required = isPrimaryKey,
                DecimalPlaces = isPrimaryKey ? 0 : decimalPlaces,
                DateModified = DateTime.UtcNow
            };
        }

        public static StringContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        public static string ApiError(string message)
        {
            return JsonSerializer.Serialize(new { Code = "ERROR", Message = message, Property = (string?)null, Entity = (string?)null });
        }

        /// <summary>
        /// Adds two entities with the same properties and the same stored values, one for the
        /// Razor page and one for the API, so a test can compare what each of them does.
        /// </summary>
        public Task<int> AddTwinsAsync(string razorName, string apiName, string? constraints, string? defaultOrder)
        {
            return Portal.WithDbContextAsync(db =>
            {
                foreach (var name in new[] { razorName, apiName })
                {
                    var twin = Entity(AppId, name, false,
                        Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                        Property("Owner", PropertyType.Number, isSystem: true, decimalPlaces: 0),
                        Property("Customer_ID", PropertyType.Number, decimalPlaces: 0),
                        Property("Code", PropertyType.String),
                        Property("Paid", PropertyType.Boolean));

                    twin.EntConstraints = constraints;
                    twin.EntDefaultOrder = defaultOrder;

                    db.Entities.Add(twin);
                }

                return db.SaveChangesAsync();
            });
        }

        /// <summary>
        /// The entities of the application with their properties, in the order they were added.
        /// </summary>
        public async Task<List<DBWS_Entity>> LoadEntitiesAsync()
        {
            var appId = AppId;

            var entities = await Portal.WithDbContextAsync(db => db.Entities
                .AsNoTracking()
                .Include(x => x.Properties)
                .Where(x => x.AppID == appId)
                .ToListAsync());

            return entities.OrderBy(x => x.ID).ToList();
        }

        public async Task<DBWS_Entity> LoadEntityAsync(string name)
        {
            return Assert.Single(await LoadEntitiesAsync(), x => x.Name == name);
        }

        public Task<int> SetStoredAsync(string entityName, Action<DBWS_Entity> change)
        {
            var appId = AppId;

            return Portal.WithDbContextAsync(async db =>
            {
                change(await db.Entities.SingleAsync(x => x.AppID == appId && x.Name == entityName));

                return await db.SaveChangesAsync();
            });
        }

        /// <summary>
        /// The audit rows a user caused, oldest first. Seeding writes none: it has no signed-in request.
        /// </summary>
        public Task<List<PortalAuditLog>> AuditRowsAsync(string userEmail)
        {
            return Portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .OrderBy(x => x.ID)
                .ToListAsync());
        }

        public static List<string> AuditShape(List<PortalAuditLog> rows, string entityName)
        {
            return rows
                .Select(x => $"{x.EntityType} | {x.EntityIdentifier.Replace(entityName, "<name>")} | {x.Action} | app {x.AppID} | {x.UserId}")
                .OrderBy(x => x)
                .ToList();
        }

        public async Task WaitForRequestsAsync(string path, int count)
        {
            for (var attempt = 0; attempt < 200 && Portal.ApiServer.RequestsTo(path).Count < count; attempt++)
            {
                await Task.Delay(50);
            }

            Assert.Equal(count, Portal.ApiServer.RequestsTo(path).Count);
        }

        /// <summary>
        /// The constraints and the default sorting of every seeded entity are as they were seeded,
        /// and nobody caused an audit row.
        /// </summary>
        public async Task AssertNothingChangedAsync()
        {
            var appId = AppId;

            Assert.Equal(
                SeedEntities(AppId).Select(x => $"{x.Name} | {x.EntConstraints} | {x.EntDefaultOrder}"),
                (await LoadEntitiesAsync()).Select(x => $"{x.Name} | {x.EntConstraints} | {x.EntDefaultOrder}"));

            Assert.False(await Portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == appId)));
        }

        /// <summary>
        /// The headers every Portal call carries: the application token, the caller's own API
        /// token and no installation key.
        /// </summary>
        public async Task AssertPortalHeadersAsync(ApiServerRequest request, string callerEmail)
        {
            var token = await Portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == callerEmail)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());

            Assert.Equal(Token, request.Headers["x-application-token"]);
            Assert.Equal("portal", request.Headers["x-client-id"]);
            Assert.Equal($"Bearer {token}", request.Headers["Authorization"]);
            Assert.False(request.Headers.ContainsKey("x-installation-key"));
        }

        public static void AssertWarning(HttpResponseMessage response)
        {
            Assert.Equal(ApiServerCacheReset.WarningText, Assert.Single(response.Headers.GetValues(ApiServerCacheReset.WarningHeaderName)));
        }

        public static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? entity = null)
        {
            Assert.Equal(status, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(code, error.Code);
            Assert.Equal(entity, error.Entity);
        }

        public static Task AssertNotFoundAsync(HttpResponseMessage response, string entity)
        {
            return AssertErrorAsync(response, HttpStatusCode.NotFound, PortalErrorCode.NotFound, entity);
        }

        /// <summary>
        /// A 400 VALIDATION with exactly these errors, as 'Property: Message', in this order.
        /// </summary>
        public static async Task AssertValidationAsync(HttpResponseMessage response, params string[] expected)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(expected, (error.Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}"));
        }
    }
}
