using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    public partial class AgentPermissionsApiTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Agent_Renames_Should_Be_Forbidden_With_All_Grants_Without_Any_Effects(bool legacyCookie)
        {
            var scene = await CreateSceneAsync();
            var agent = scene.Agent;
            var catalogue = (await ReadPermissionsAsync(agent, scene.First)).Resources;
            var grants = catalogue.Select(x => Grant(x.Resource, x.CanRead, x.CanWrite, x.CanDelete)).ToArray();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, grants);

            if (legacyCookie)
            {
                // Even an old agent account retaining a password and admin role stays refused.
                var (oldEmail, password) = await _portal.CreateAdminUserAsync();
                var email = $"legacy-rename-{Guid.NewGuid():N}@agent.local";
                await _portal.WithDbContextAsync(async db =>
                {
                    var user = await db.Users.SingleAsync(x => x.Email == oldEmail);
                    user.Email = email;
                    user.UserName = email;
                    user.NormalizedEmail = email.ToUpperInvariant();
                    user.NormalizedUserName = email.ToUpperInvariant();
                    var share = new DBWS_Collaborate { AppID = scene.First.ID, UserEmail = email, DateModified = DateTime.UtcNow };
                    db.Collaborations.Add(share);
                    db.AgentPermissions.Add(new PortalAgentPermission { Collaboration = share, PermissionsJson = JsonSerializer.Serialize(grants) });
                    return await db.SaveChangesAsync();
                });
                agent = await _portal.CreateSignedInClientAsync(email, password);
            }

            var schemaBefore = await scene.Schema.StoredSchemaAsync(scene.First);
            var auditBefore = await scene.Schema.AuditAsync(scene.First);
            var discovery = await ReadPermissionsAsync(agent, scene.First);
            foreach (var route in new[] { "/entities/Orders/rename", "/entities/Orders/properties/Amount/rename" })
            {
                var response = await agent.PostAsync(Url(scene.First) + route, new { NewName = "RenamedValue" }.ToJsonContent());
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal(PortalAgent.RefusedMessage, (await response.ReadJsonAsync<ErrorResponse>()).Message);
            }

            foreach (var route in new[] { "/entities/{entity}/rename", "/entities/{entity}/properties/{property}/rename" })
            {
                var operation = Operation(discovery, "POST", route);
                Assert.False(operation.AllowedForThisApplication);
                Assert.Empty(operation.Requirements);
            }
            Assert.Equal(schemaBefore, await scene.Schema.StoredSchemaAsync(scene.First));
            Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Retired_Mail_Write_Grant_Should_Keep_Other_Valid_Saved_Permissions()
        {
            var scene = await CreateSceneAsync();
            await _portal.WithDbContextAsync(async db =>
            {
                var policy = await db.AgentPermissions.SingleAsync(x => x.CollaborationId == scene.FirstCollaborationId);
                policy.PermissionsJson = JsonSerializer.Serialize(new[]
                {
                    Grant("email-settings", read: true, write: true),
                    Grant("entities", read: true, write: true, delete: true)
                });
                return await db.SaveChangesAsync();
            });

            var discovery = await ReadPermissionsAsync(scene.Agent, scene.First);
            var mail = Assert.Single(discovery.Permissions, x => x.Resource == "email-settings");
            Assert.True(mail.Read);
            Assert.False(mail.Write);
            Assert.False(mail.Delete);
            Assert.False(Assert.Single(discovery.Resources, x => x.Resource == "email-settings").CanWrite);
            var entities = Assert.Single(discovery.Permissions, x => x.Resource == "entities");
            Assert.True(entities.Read && entities.Write && entities.Delete);
            Assert.True(Operation(discovery, "PUT", "/entities/{entity}").AllowedForThisApplication);
            Assert.False(Operation(discovery, "PUT", "/email-settings").AllowedForThisApplication);
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.First)}/entities")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.First)}/email-settings")).StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }
    }
}
