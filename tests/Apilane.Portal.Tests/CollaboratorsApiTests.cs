using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// Sharing an application. Some tests switch the instance mail on; the seeded state (no mail
    /// settings, registration allowed) is put back after every test.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class CollaboratorsApiTests : IAsyncLifetime
    {
        private const string ApplicationsUrl = "/api/v1/applications";

        private readonly PortalFactory _portal;

        public CollaboratorsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        public Task InitializeAsync()
        {
            return _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);
        }

        public Task DisposeAsync()
        {
            return _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: false);
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Owner_Should_Return_The_Collaborators_In_The_Order_They_Were_Added()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(Url(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<CollaboratorResponse>>();
            var stored = await StoredCollaboratorsAsync(scene.Application.Application.ID);

            Assert.Equal(new[] { scene.CollaboratorEmail, "zz-first@portal.test", "aa-second@portal.test" }, list.Data.Select(x => x.Email));
            Assert.Equal(stored.Select(x => x.ID), list.Data.Select(x => x.ID));
            Assert.Equal(3, list.Total);
        }

        [Fact]
        public async Task List_Of_An_Application_Shared_With_Nobody_Should_Be_Empty()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "not-shared");
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);

            var list = await (await owner.GetAsync(Url(application))).ReadJsonAsync<ListResponse<CollaboratorResponse>>();

            Assert.Empty(list.Data);
            Assert.Equal(0, list.Total);
        }

        // ---------- Add ----------

        [Fact]
        public async Task Add_Should_Trim_The_Email_Store_It_And_Return_201_Without_A_Notification_When_Mail_Is_Not_Configured()
        {
            var scene = await CreateSceneAsync();
            var email = NewEmail();

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = $"  {email} " }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var added = await response.ReadJsonAsync<CollaboratorAddedResponse>();
            Assert.Equal(email, added.Email);
            Assert.False(added.NotificationSent);
            Assert.Empty(_portal.Mail.SentTo(email));

            var stored = Assert.Single(await StoredCollaboratorsAsync(scene.Application.Application.ID), x => x.UserEmail == email);
            Assert.Equal(added.ID, stored.ID);

            var list = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<ListResponse<CollaboratorResponse>>();
            Assert.Equal(email, list.Data.Last().Email);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Add_With_Mail_Configured_Should_Mail_The_Address_With_A_Link_To_The_Ui()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var email = NewEmail();

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.True((await response.ReadJsonAsync<CollaboratorAddedResponse>()).NotificationSent);

            var mail = Assert.Single(_portal.Mail.SentTo(email));
            Assert.Equal("smtp.portal.test", mail.MailServer);
            Assert.Equal("portal@portal.test", mail.MailFromAddress);
            Assert.Equal($"Apilane tests - admin rights to {scene.Application.Application.Name}", mail.Subject);
            Assert.Contains($"User {scene.OwnerEmail} shared administrator rights to application <b>{scene.Application.Application.Name}</b> with you.", mail.Body);
            Assert.Contains("<a href='http://localhost/apps'>portal</a>", mail.Body);
        }

        [Fact]
        public async Task Add_Should_Html_Encode_The_Application_Name_And_Owner_Email_In_The_Mail()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "<i>tagged</i>");
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword);
            var email = NewEmail();

            // Access goes by user id, so only the mail sees this address.
            await _portal.WithDbContextAsync(async db =>
            {
                var stored = await db.Applications.SingleAsync(x => x.ID == application.Application.ID);
                stored.AdminEmail = "<b>o</b>@portal.test";
                return await db.SaveChangesAsync();
            });

            var response = await owner.PostAsync(Url(application), new { Email = email }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var mail = Assert.Single(_portal.Mail.SentTo(email));
            Assert.Contains("<b>&lt;i&gt;tagged&lt;/i&gt;</b>", mail.Body);
            Assert.Contains("User &lt;b&gt;o&lt;/b&gt;@portal.test shared", mail.Body);
            // The subject is plain text.
            Assert.Equal("Apilane tests - admin rights to <i>tagged</i>", mail.Subject);
        }

        [Fact]
        public async Task Add_When_Sending_Throws_Should_Still_Store_The_Share_And_Return_NotificationSent_False()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);

            var mailer = A.Fake<IEmailService>();
            A.CallTo(() => mailer.SendMail(A<EmailInfo>._)).Throws(new InvalidOperationException("The mail failed on purpose."));

            var host = _portal.CreateHost(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton(mailer);
            });

            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "mail-fails");
            var session = await _portal.SignInAsync(ownerEmail, ownerPassword, host);
            var email = NewEmail();

            var response = await session.Client.PostAsync(Url(application), new { Email = email }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var added = await response.ReadJsonAsync<CollaboratorAddedResponse>();
            Assert.False(added.NotificationSent);
            A.CallTo(() => mailer.SendMail(A<EmailInfo>._)).MustHaveHappenedOnceExactly();

            var stored = Assert.Single(await StoredCollaboratorsAsync(application.Application.ID), x => x.UserEmail == email);
            Assert.Equal(added.ID, stored.ID);
        }

        [Fact]
        public async Task Add_Should_Write_A_Created_Audit_Row()
        {
            var scene = await CreateSceneAsync();
            var email = NewEmail();

            await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent());

            var row = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Collaboration", row.EntityType);
            Assert.Equal("Created", row.Action);
            Assert.Equal(email, row.EntityIdentifier);
            Assert.Equal(scene.Application.Application.ID, row.AppID);
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("user@")]
        [InlineData("two words@portal.test")]
        [InlineData("Name <user@portal.test>")]
        public async Task Add_With_An_Invalid_Email_Should_Return_400_And_Store_Nothing(string email)
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent());

            await AssertValidationAsync(response, "Email", CollaboratorService.InvalidEmailMessage);
            Assert.Equal(3, (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Count);
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Add_Without_An_Email_Should_Return_400_Required(string email)
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent());

            await AssertValidationAsync(response, "Email", "Required");
        }

        [Fact]
        public async Task Add_With_A_Duplicate_That_Differs_Only_In_Case_Should_Return_409()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var email = scene.CollaboratorEmail.ToUpperInvariant();

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = $" {email}" }.ToJsonContent());

            await AssertConflictAsync(response, CollaboratorService.AlreadySharedMessage);
            Assert.Equal(3, (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Count);
            // The service trims the address, so a stray mail would go to the trimmed one.
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Fact]
        public async Task Add_With_An_Email_Already_On_Another_Application_Should_Return_201()
        {
            var scene = await CreateSceneAsync();

            // Another application of the same owner, so a check across the owner's applications would fail too.
            var server = await _portal.CreateServerAsync();
            var other = await _portal.CreateApplicationAsync(server.ID, scene.OwnerEmail, "other-app");

            var response = await scene.Owner.PostAsync(Url(other), new { Email = scene.CollaboratorEmail }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Single(await StoredCollaboratorsAsync(other.Application.ID), x => x.UserEmail == scene.CollaboratorEmail);
            Assert.Equal(3, (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Add_With_The_Owners_Own_Email_Should_Return_409(bool upperCase)
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var email = upperCase ? scene.OwnerEmail.ToUpperInvariant() : scene.OwnerEmail;

            var response = await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent());

            await AssertConflictAsync(response, CollaboratorService.SelfShareMessage);
            Assert.Equal(3, (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Count);
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Fact]
        public async Task Added_User_Should_See_The_Application_As_Not_Owned_And_Lose_It_After_Delete()
        {
            var scene = await CreateSceneAsync();
            var (email, password) = await _portal.CreateUserAsync();
            var newCollaborator = await _portal.CreateSignedInClientAsync(email, password);
            var token = scene.Application.Application.Token;

            var before = await (await newCollaborator.GetAsync(ApplicationsUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            Assert.DoesNotContain(before.Data, x => x.Token == token);

            var added = await (await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent())).ReadJsonAsync<CollaboratorAddedResponse>();

            var shared = await (await newCollaborator.GetAsync(ApplicationsUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            var application = Assert.Single(shared.Data, x => x.Token == token);
            Assert.False(application.IsOwner);

            // A collaborator may not share further.
            await AssertForbiddenAsync(await newCollaborator.GetAsync(Url(scene)));

            var delete = await scene.Owner.DeleteAsync($"{Url(scene)}/{added.ID}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            var after = await (await newCollaborator.GetAsync(ApplicationsUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            Assert.DoesNotContain(after.Data, x => x.Token == token);
            await AssertNotFoundAsync(await newCollaborator.GetAsync($"{ApplicationsUrl}/{token}"), "Application");
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Remove_Only_That_Collaborator_And_Write_A_Deleted_Audit_Row()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var stored = await StoredCollaboratorsAsync(scene.Application.Application.ID);
            var removed = stored.Single(x => x.UserEmail == "zz-first@portal.test");

            var response = await scene.Owner.DeleteAsync($"{Url(scene)}/{removed.ID}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(
                stored.Where(x => x.ID != removed.ID).Select(x => x.ID),
                (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Select(x => x.ID));

            var row = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Collaboration", row.EntityType);
            Assert.Equal("Deleted", row.Action);
            Assert.Equal("zz-first@portal.test", row.EntityIdentifier);
            Assert.Equal(scene.Application.Application.ID, row.AppID);
            Assert.Empty(_portal.ApiServer.Requests);
            // No test mails this fixed address, so any mail here came from the delete.
            Assert.Empty(_portal.Mail.SentTo("zz-first@portal.test"));
        }

        [Fact]
        public async Task Delete_With_The_Id_Of_Another_Application_Should_Return_404_And_Keep_It()
        {
            var scene = await CreateSceneAsync();

            // Another application of the same owner, so only the route decides.
            var server = await _portal.CreateServerAsync();
            var other = await _portal.CreateApplicationAsync(server.ID, scene.OwnerEmail, "other-app", "kept@portal.test");
            var otherCollaborator = Assert.Single(await StoredCollaboratorsAsync(other.Application.ID));

            var response = await scene.Owner.DeleteAsync($"{Url(scene)}/{otherCollaborator.ID}");

            await AssertNotFoundAsync(response, "Collaborator");
            Assert.Single(await StoredCollaboratorsAsync(other.Application.ID));
        }

        [Fact]
        public async Task Delete_With_An_Unknown_Id_Should_Return_404()
        {
            var scene = await CreateSceneAsync();

            await AssertNotFoundAsync(await scene.Owner.DeleteAsync($"{Url(scene)}/987654321"), "Collaborator");
        }

        // ---------- Access ----------

        [Fact]
        public async Task Collaborator_Should_Get_403_On_List_Add_And_Delete_And_Change_Nothing()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var stored = await StoredCollaboratorsAsync(scene.Application.Application.ID);
            var email = NewEmail();

            await AssertForbiddenAsync(await scene.Collaborator.GetAsync(Url(scene)));
            await AssertForbiddenAsync(await scene.Collaborator.PostAsync(Url(scene), new { Email = email }.ToJsonContent()));
            await AssertForbiddenAsync(await scene.Collaborator.DeleteAsync($"{Url(scene)}/{stored[0].ID}"));

            Assert.Equal(stored.Select(x => x.ID), (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Select(x => x.ID));
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        [Fact]
        public async Task Stranger_And_Admin_Who_Are_Not_Members_Should_Get_404_On_List_Add_And_Delete()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var stored = await StoredCollaboratorsAsync(scene.Application.Application.ID);
            var admin = await _portal.CreateAdminClientAsync();

            foreach (var client in new[] { scene.Stranger, admin })
            {
                var email = NewEmail();

                await AssertNotFoundAsync(await client.GetAsync(Url(scene)), "Application");
                await AssertNotFoundAsync(await client.PostAsync(Url(scene), new { Email = email }.ToJsonContent()), "Application");
                await AssertNotFoundAsync(await client.DeleteAsync($"{Url(scene)}/{stored[0].ID}"), "Application");
                Assert.Empty(_portal.Mail.SentTo(email));
            }

            Assert.Equal(stored.Select(x => x.ID), (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Select(x => x.ID));
        }

        [Fact]
        public async Task Anonymous_Should_Get_401_On_List_Add_And_Delete()
        {
            var scene = await CreateSceneAsync();
            var anonymous = _portal.CreateAnonymousClient();

            await AssertUnauthorizedAsync(await anonymous.GetAsync(Url(scene)));
            await AssertUnauthorizedAsync(await anonymous.PostAsync(Url(scene), new { Email = NewEmail() }.ToJsonContent()));
            await AssertUnauthorizedAsync(await anonymous.DeleteAsync($"{Url(scene)}/1"));
        }

        [Fact]
        public async Task Writes_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            await _portal.SetAccountSettingsAsync(allowRegister: true, mailConfigured: true);
            var scene = await CreateSceneAsync();
            var stored = await StoredCollaboratorsAsync(scene.Application.Application.ID);
            var email = NewEmail();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            foreach (var response in new[]
            {
                await scene.Owner.PostAsync(Url(scene), new { Email = email }.ToJsonContent()),
                await scene.Owner.DeleteAsync($"{Url(scene)}/{stored[0].ID}")
            })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            Assert.Equal(stored.Select(x => x.ID), (await StoredCollaboratorsAsync(scene.Application.Application.ID)).Select(x => x.ID));
            Assert.Empty(_portal.Mail.SentTo(email));
        }

        // ---------- Helpers ----------

        private record Scene(
            string OwnerEmail,
            HttpClient Owner,
            string CollaboratorEmail,
            HttpClient Collaborator,
            HttpClient Stranger,
            SeededApplication Application);

        /// <summary>
        /// Three new users. The owner's application is shared with the collaborator and with two
        /// addresses without an account, added in the opposite order of their names.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            var application = await _portal.CreateApplicationAsync(
                server.ID, ownerEmail, "shared-app", collaboratorEmail, "zz-first@portal.test", "aa-second@portal.test");

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                application);
        }

        private static string Url(Scene scene)
        {
            return Url(scene.Application);
        }

        private static string Url(SeededApplication application)
        {
            return $"{ApplicationsUrl}/{application.Application.Token}/collaborators";
        }

        private static string NewEmail()
        {
            return $"shared-{Guid.NewGuid():N}@portal.test";
        }

        private Task<List<DBWS_Collaborate>> StoredCollaboratorsAsync(long appId)
        {
            return _portal.WithDbContextAsync(db => db.Collaborations
                .AsNoTracking()
                .Where(x => x.AppID == appId)
                .OrderBy(x => x.ID)
                .ToListAsync());
        }

        private Task<List<PortalAuditLog>> AuditRowsAsync(string userEmail)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .OrderBy(x => x.ID)
                .ToListAsync());
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal(property, detail.Property);
            Assert.Equal(message, detail.Message);
        }

        private static async Task AssertConflictAsync(HttpResponseMessage response, string message)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal(message, error.Message);
            Assert.Equal("Collaborator", error.Entity);
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        private static async Task AssertNotFoundAsync(HttpResponseMessage response, string entity)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal(entity, error.Entity);
        }

        private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
