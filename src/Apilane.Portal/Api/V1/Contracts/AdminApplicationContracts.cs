using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// An application of any user of the instance, as an administrator sees it. It never carries the
    /// encryption key, the connection string or the mail password: HasConnectionString and
    /// HasMailPassword say whether they are set.
    /// </summary>
    public class AdminApplicationResponse
    {
        [Required]
        public long ID { get; set; }

        /// <summary>
        /// Identifies the application, in this API and on its API server (header x-application-token).
        /// </summary>
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The e-mail address of the user who created the application.
        /// </summary>
        public string? OwnerEmail { get; set; }

        /// <summary>
        /// Whether the API server accepts calls for the application.
        /// </summary>
        [Required]
        public bool Online { get; set; }

        /// <summary>
        /// The API server that hosts the application.
        /// </summary>
        [Required]
        public ServerSummaryResponse Server { get; set; } = new ServerSummaryResponse();

        /// <summary>
        /// Where the application's data lives: SQLLite, SQLServer, MySQL or PostgreSQL.
        /// </summary>
        [Required]
        public string DatabaseType { get; set; } = string.Empty;

        /// <summary>
        /// Whether a connection string is stored. The value itself is never returned. Always false
        /// for SQLLite, whose database file the API server names itself.
        /// </summary>
        [Required]
        public bool HasConnectionString { get; set; }

        [Required]
        public int MaxAllowedFileSizeInKB { get; set; }

        [Required]
        public int AuthTokenExpireMinutes { get; set; }

        /// <summary>
        /// Where a user lands after confirming their e-mail address, or null for the default page.
        /// </summary>
        public string? EmailConfirmationRedirectUrl { get; set; }

        public string? MailServer { get; set; }

        public int? MailServerPort { get; set; }

        public string? MailFromAddress { get; set; }

        public string? MailUserName { get; set; }

        /// <summary>
        /// Whether a mail password is stored. The value itself is never returned.
        /// </summary>
        [Required]
        public bool HasMailPassword { get; set; }

        public string? MailFromDisplayName { get; set; }
    }

    /// <summary>
    /// An application of any user of the instance with its entities and their properties, for the
    /// administrator's data browser.
    /// </summary>
    public class AdminApplicationDetailResponse
    {
        [Required]
        public AdminApplicationResponse Application { get; set; } = new AdminApplicationResponse();

        /// <summary>
        /// The custom and system entities by name, each with its properties (the primary key first,
        /// then custom ones, then system ones, each group by name). Empty when it has none.
        /// </summary>
        [Required]
        public List<EntityResponse> Entities { get; set; } = new List<EntityResponse>();
    }
}
