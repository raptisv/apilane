using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// Three new users and one server, for the tests of the schema import and the application
    /// comparison: both work on two applications at once. A test adds the applications it needs
    /// with <see cref="AddApplicationAsync"/>.
    /// </summary>
    public record SchemaScene(
        PortalFactory Portal,
        string OwnerEmail,
        HttpClient Owner,
        string CollaboratorEmail,
        HttpClient Collaborator,
        string StrangerEmail,
        HttpClient Stranger,
        DBWS_Server Server)
    {
        public const string OwnerConstraint = "{\"IsSystem\":true,\"TypeID\":2,\"Properties\":\"Owner,Users,ON_DELETE_SET_NULL\"}";

        public static async Task<SchemaScene> CreateAsync(PortalFactory portal)
        {
            var (ownerEmail, ownerPassword) = await portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await portal.CreateUserAsync();

            return new SchemaScene(
                portal,
                ownerEmail,
                await portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                strangerEmail,
                await portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                await portal.CreateServerAsync());
        }

        public static string AppUrl(DBWS_Application application) => $"/api/v1/applications/{application.Token}";

        /// <summary>
        /// Adds an application of the owner, shared with the collaborator, with the entities,
        /// custom endpoints and security rules <paramref name="fill"/> gives it.
        /// </summary>
        public Task<DBWS_Application> AddApplicationAsync(string name, Action<DBWS_Application> fill)
        {
            return AddApplicationAsync(name, OwnerEmail, new[] { CollaboratorEmail }, fill);
        }

        public async Task<DBWS_Application> AddApplicationAsync(string name, string ownerEmail, string[] collaboratorEmails, Action<DBWS_Application> fill)
        {
            var seeded = await Portal.CreateApplicationAsync(Server.ID, ownerEmail, $"{name}-{Guid.NewGuid():N}", collaboratorEmails);
            var appId = seeded.Application.ID;

            return await Portal.WithDbContextAsync(async db =>
            {
                var application = await db.Applications.SingleAsync(x => x.ID == appId);

                application.Entities = new List<DBWS_Entity>();
                application.CustomEndpoints = new List<DBWS_CustomEndpoint>();

                fill(application);

                await db.SaveChangesAsync();

                return application;
            });
        }

        /// <summary>
        /// The entity every application has.
        /// </summary>
        public static DBWS_Entity Users()
        {
            return EntityScene.Entity(0, "Users", true,
                EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                EntityScene.Property("Email", PropertyType.String, isSystem: true));
        }

        /// <summary>
        /// A custom entity with the system properties and the system constraint a new entity
        /// gets, followed by <paramref name="properties"/>; <paramref name="constraints"/> are
        /// stored after the system one, as JSON objects.
        /// </summary>
        public static DBWS_Entity Custom(string name, DBWS_EntityProperty[] properties, params string[] constraints)
        {
            var all = new List<DBWS_EntityProperty>
            {
                EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                EntityScene.Property("Owner", PropertyType.Number, isSystem: true, decimalPlaces: 0),
                EntityScene.Property("Created", PropertyType.Date, isSystem: true)
            };

            all.AddRange(properties);

            var entity = EntityScene.Entity(0, name, false, all.ToArray());
            entity.EntConstraints = $"[{string.Join(",", new[] { OwnerConstraint }.Concat(constraints))}]";

            return entity;
        }

        public static string Unique(string properties)
        {
            return $"{{\"IsSystem\":false,\"TypeID\":1,\"Properties\":\"{properties}\"}}";
        }

        public static string ForeignKey(string properties)
        {
            return $"{{\"IsSystem\":false,\"TypeID\":2,\"Properties\":\"{properties}\"}}";
        }

        public static DBWS_CustomEndpoint Endpoint(string name, string query, string? description = null)
        {
            return new DBWS_CustomEndpoint { Name = name, Query = query, Description = description };
        }

        public static DBWS_Security Rule(
            SecurityTypes type,
            string name,
            string role,
            string action,
            EndpointRecordAuthorization record = EndpointRecordAuthorization.All,
            string? properties = null,
            DBWS_Security.RateLimitItem? rateLimit = null)
        {
            return new DBWS_Security
            {
                TypeID = (int)type,
                Name = name,
                RoleID = role,
                Action = action,
                Record = (int)record,
                Properties = properties,
                RateLimit = rateLimit
            };
        }

        /// <summary>
        /// The rules as the Security column stores them.
        /// </summary>
        public static string Security(params DBWS_Security[] rules)
        {
            return JsonSerializer.Serialize(rules.ToList());
        }

        /// <summary>
        /// The API server answers the calls of a schema import the way a healthy one does. What
        /// it answers for a new entity is numbered -1, as it returns it.
        /// </summary>
        public void ScriptApiServer()
        {
            var id = EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true);
            var owner = EntityScene.Property("Owner", PropertyType.Number, isSystem: true, decimalPlaces: 0);
            var created = EntityScene.Property("Created", PropertyType.Date, isSystem: true);

            foreach (var property in new[] { id, owner, created })
            {
                property.ID = -1;
                property.EntityID = -1;
                property.DateModified = default;
            }

            var initial = new EntityPropertiesConstrainsDto
            {
                Properties = new List<DBWS_EntityProperty> { id, owner, created },
                Constraints = new List<EntityConstraint>
                {
                    new EntityConstraint { IsSystem = true, TypeID = (int)ConstraintType.ForeignKey, Properties = "Owner,Users,ON_DELETE_SET_NULL" }
                }
            };

            Portal.ApiServer.Respond(FakeApiServer.GetSystemPropertiesAndConstraintsPath, HttpStatusCode.OK, JsonSerializer.Serialize(initial));
            Portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.OK, string.Empty);
            Portal.ApiServer.Respond(FakeApiServer.GeneratePropertyPath, HttpStatusCode.OK, string.Empty);
            Portal.ApiServer.Respond(FakeApiServer.GenerateConstraintsPath, HttpStatusCode.OK, string.Empty);
            Portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        /// <summary>
        /// Everything the import can change in an application, as text: its entities in the order
        /// they were created, each with its constraints and its properties, its security rules and
        /// its custom endpoints. Without IDs, so two applications can be compared.
        /// </summary>
        public async Task<string> StoredSchemaAsync(DBWS_Application application)
        {
            var appId = application.ID;

            var stored = await Portal.WithDbContextAsync(db => db.Applications
                .AsNoTracking()
                .Include(x => x.Entities).ThenInclude(x => x.Properties)
                .Include(x => x.CustomEndpoints)
                .AsSplitQuery()
                .SingleAsync(x => x.ID == appId));

            var lines = new List<string>();

            foreach (var entity in stored.Entities.OrderBy(x => x.ID))
            {
                lines.Add($"entity {entity.Name} | {entity.Description} | tracking {entity.RequireChangeTracking} | differentiation {entity.HasDifferentiationProperty} | system {entity.IsSystem} | read-only {entity.IsReadOnly} | {entity.EntConstraints}");

                lines.AddRange(entity.Properties.OrderBy(x => x.ID).Select(x =>
                    $"  property {x.Name} | type {x.TypeID} | required {x.Required} | min {x.Minimum} | max {x.Maximum} | decimals {x.DecimalPlaces} | encrypted {x.Encrypted} | regex {x.ValidationRegex} | {x.Description} | system {x.IsSystem} | key {x.IsPrimaryKey}"));
            }

            lines.Add($"security {stored.Security}");
            lines.AddRange(stored.CustomEndpoints.OrderBy(x => x.ID).Select(x => $"endpoint {x.Name} | {x.Description} | {x.Query}"));

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// The audit rows of an application, oldest first, as 'type | identifier | action | user'.
        /// Seeding writes none: it has no signed-in request.
        /// </summary>
        public async Task<List<string>> AuditAsync(DBWS_Application application)
        {
            var appId = application.ID;

            var rows = await Portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.AppID == appId)
                .OrderBy(x => x.ID)
                .ToListAsync());

            return rows.Select(x => $"{x.EntityType} | {x.EntityIdentifier} | {x.Action} | {x.UserEmail}").ToList();
        }

        public static async Task<ErrorResponse> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Assert.Equal(status, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(code, error.Code);

            return error;
        }

        public static async Task AssertNotFoundAsync(HttpResponseMessage response)
        {
            var error = await AssertErrorAsync(response, HttpStatusCode.NotFound, PortalErrorCode.NotFound);

            Assert.Equal("Application", error.Entity);
        }

        /// <summary>
        /// A 400 VALIDATION with exactly these errors, as 'Property: Message', in this order.
        /// </summary>
        public static async Task<ErrorResponse> AssertValidationAsync(HttpResponseMessage response, params string[] expected)
        {
            var error = await AssertErrorAsync(response, HttpStatusCode.BadRequest, PortalErrorCode.Validation);

            Assert.Equal(expected, (error.Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}"));

            return error;
        }
    }
}
