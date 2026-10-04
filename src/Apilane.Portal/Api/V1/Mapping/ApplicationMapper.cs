using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;

namespace Apilane.Portal.Api.V1.Mapping
{
    public static class ApplicationMapper
    {
        /// <summary>
        /// The application as the API shows it. Server, Collaborates and CustomEndpoints must be loaded.
        /// </summary>
        public static ApplicationResponse ToResponse(this DBWS_Application application, string currentUserId)
        {
            var isOwner = application.UserID == currentUserId;

            return new ApplicationResponse
            {
                Token = application.Token,
                Name = application.Name,
                Online = application.Online,
                DatabaseType = ((DatabaseType)application.DatabaseType).ToString(),
                HasConnectionString = application.HasConnectionString(),
                DifferentiationEntity = string.IsNullOrWhiteSpace(application.DifferentiationEntity) ? null : application.DifferentiationEntity,
                MaxAllowedFileSizeInKB = application.MaxAllowedFileSizeInKB,
                IsOwner = isOwner,
                OwnerEmail = application.AdminEmail,
                CollaboratorCount = isOwner ? application.Collaborates.Count : null,
                CustomEndpointCount = application.CustomEndpoints.Count,
                Server = application.Server.ToSummaryResponse()
            };
        }

        /// <summary>
        /// The application as the administrator's list shows it, without any secret. Server must be loaded.
        /// </summary>
        public static AdminApplicationResponse ToAdminResponse(this DBWS_Application application)
        {
            return new AdminApplicationResponse
            {
                ID = application.ID,
                Token = application.Token,
                Name = application.Name,
                OwnerEmail = application.AdminEmail,
                Online = application.Online,
                Server = application.Server.ToSummaryResponse(),
                DatabaseType = ((DatabaseType)application.DatabaseType).ToString(),
                HasConnectionString = application.HasConnectionString(),
                MaxAllowedFileSizeInKB = application.MaxAllowedFileSizeInKB,
                AuthTokenExpireMinutes = application.AuthTokenExpireMinutes,
                EmailConfirmationRedirectUrl = application.EmailConfirmationRedirectUrl,
                MailServer = application.MailServer,
                MailServerPort = application.MailServerPort,
                MailFromAddress = application.MailFromAddress,
                MailUserName = application.MailUserName,
                HasMailPassword = !string.IsNullOrWhiteSpace(application.MailPassword),
                MailFromDisplayName = application.MailFromDisplayName
            };
        }

        public static ServerSummaryResponse ToSummaryResponse(this DBWS_Server server)
        {
            return new ServerSummaryResponse
            {
                ID = server.ID,
                Name = server.Name,
                ServerUrl = server.ServerUrl
            };
        }

        // A tampered Razor Edit form can leave one on an SQLite application; the API server ignores it there.
        private static bool HasConnectionString(this DBWS_Application application)
        {
            return application.DatabaseType != (int)DatabaseType.SQLLite && !string.IsNullOrWhiteSpace(application.ConnectionString);
        }
    }
}
