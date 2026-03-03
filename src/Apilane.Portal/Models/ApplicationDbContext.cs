using Apilane.Common;
using Apilane.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ILogger<ApplicationDbContext> _logger;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public DbSet<GlobalSettings> GlobalSettings { get; set; }
        public DbSet<DBWS_Server> Servers { get; set; }
        public DbSet<DBWS_Application> Applications { get; set; }
        public DbSet<DBWS_Entity> Entities { get; set; }
        public DbSet<DBWS_EntityProperty> EntityProperties { get; set; }
        public DbSet<DBWS_CustomEndpoint> CustomEndpoints { get; set; }
        public DbSet<DBWS_Collaborate> Collaborations { get; set; }
        public DbSet<DBWS_ReportItem> Reports { get; set; }
        public DbSet<PortalAuditLog> AuditLogs { get; set; }

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ILogger<ApplicationDbContext> logger,
            IHttpContextAccessor? httpContextAccessor = null)
            : base(options)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Customize the ASP.NET Core Identity model and override the defaults if needed.
            // For example, you can rename the ASP.NET Core Identity table names and more.
            // Add your customizations after calling base.OnModelCreating(builder);

            var portalConfig = Database.GetService<PortalConfiguration>();

            builder.Entity<GlobalSettings>().ToTable("GlobalSettings").HasKey(p => p.ID);
            builder.Entity<GlobalSettings>().Property(m => m.MailFromAddress).IsRequired(false);
            builder.Entity<GlobalSettings>().Property(m => m.MailFromDisplayName).IsRequired(false);
            builder.Entity<GlobalSettings>().Property(m => m.MailPassword).IsRequired(false);
            builder.Entity<GlobalSettings>().Property(m => m.MailServer).IsRequired(false);
            builder.Entity<GlobalSettings>().Property(m => m.MailUserName).IsRequired(false);
            builder.Entity<GlobalSettings>().Property(m => m.MailServerPort).IsRequired(false);

            builder.Entity<DBWS_Server>().ToTable("Servers").HasKey(p => p.ID);

            builder.Entity<DBWS_Application>()
                .Ignore(p => p.Security_List)
                .ToTable("Applications")
                .HasKey(p => p.ID);

            builder.Entity<DBWS_Application>().HasOne(p => p.Server).WithMany(b => b.Applications).HasForeignKey(p => p.ServerID).IsRequired();

            builder.Entity<DBWS_Entity>()
                .Ignore(p => p.Constraints)
                .Ignore(p => p.DefaultOrder)
                .ToTable("Entities")
                .HasKey(p => p.ID);

            builder.Entity<DBWS_Entity>().HasIndex(x => new { x.AppID, x.Name }).IsUnique(true);
            builder.Entity<DBWS_Entity>().HasOne(p => p.Application).WithMany(b => b.Entities).HasForeignKey(p => p.AppID).IsRequired();

            builder.Entity<DBWS_EntityProperty>().ToTable("EntityProperties").HasKey(p => p.ID);
            builder.Entity<DBWS_EntityProperty>().HasOne(p => p.Entity).WithMany(b => b.Properties).HasForeignKey(p => p.EntityID).IsRequired();

            builder.Entity<DBWS_CustomEndpoint>().ToTable("CustomEndpoints").HasKey(p => p.ID);
            builder.Entity<DBWS_CustomEndpoint>().HasOne(p => p.Application).WithMany(b => b.CustomEndpoints).HasForeignKey(p => p.AppID).IsRequired();

            builder.Entity<DBWS_Collaborate>().ToTable("Collaborations").HasKey(p => p.ID);
            builder.Entity<DBWS_Collaborate>().HasOne(p => p.Application).WithMany(b => b.Collaborates).HasForeignKey(p => p.AppID).IsRequired();

            builder.Entity<DBWS_ReportItem>().ToTable("Reports").HasKey(p => p.ID);
            builder.Entity<DBWS_ReportItem>().HasOne(p => p.Application).WithMany(b => b.Reports).HasForeignKey(p => p.AppID).IsRequired();

            // Audit logs table
            builder.Entity<PortalAuditLog>().ToTable("AuditLogs").HasKey(p => p.ID);
            builder.Entity<PortalAuditLog>().Property(p => p.Timestamp).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.UserId).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.UserEmail).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.AppID).IsRequired(false);
            builder.Entity<PortalAuditLog>().Property(p => p.EntityType).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.EntityIdentifier).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.Action).IsRequired();
            builder.Entity<PortalAuditLog>().Property(p => p.Changes).IsRequired(false);
            builder.Entity<PortalAuditLog>().HasIndex(p => p.AppID);
            builder.Entity<PortalAuditLog>().HasIndex(p => p.Timestamp);

            var adminRoleGuid = Guid.NewGuid().ToString("D");
            var adminUserGuid = Guid.NewGuid().ToString("D");

            // Seed Admin role
            builder.Entity<IdentityRole>().HasData(
                new IdentityRole() { Id = adminRoleGuid, Name = Globals.AdminRoleName, ConcurrencyStamp = "1", NormalizedName = Globals.AdminRoleName });

            // Seed admin user
            ApplicationUser adminUser = new ApplicationUser()
            {
                Id = adminUserGuid,
                UserName = portalConfig.AdminEmail,
                Email = portalConfig.AdminEmail,
                NormalizedEmail = portalConfig.AdminEmail.ToUpper(),
                NormalizedUserName = portalConfig.AdminEmail.ToUpper(),
                LockoutEnabled = false,
                LastLogin = DateTime.UtcNow,
                DateRegistered = DateTime.UtcNow,
                TwoFactorEnabled = false,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("D")
            };
            PasswordHasher<ApplicationUser> passwordHasher = new PasswordHasher<ApplicationUser>();
            var initialAdminPassword = "admin";
            adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, initialAdminPassword);

            builder.Entity<ApplicationUser>().HasData(adminUser);

            // Bind Admin to role
            builder.Entity<IdentityUserRole<string>>().HasData(
                new IdentityUserRole<string>() { RoleId = adminRoleGuid, UserId = adminUserGuid });

            // Seed initial global settings
            builder.Entity<GlobalSettings>().HasData(new GlobalSettings
            {
                ID = 1,
                InstanceTitle = portalConfig.InstanceTitle,
                InstallationKey = portalConfig.InstallationKey,
                AllowRegisterToPortal = true,
                MailFromAddress = null,
                MailFromDisplayName = null,
                MailPassword = null,
                MailServer = null,
                MailServerPort = null,
                MailUserName = null
            });

            // Seed initial server
            builder.Entity<DBWS_Server>().HasData(new DBWS_Server
            {
                ID = 1,
                Name = "My server",
                ServerUrl = portalConfig.ApiUrl,
                DateModified = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Ensures all tables, columns, and indexes defined in the EF model exist
        /// in an already-created database. Derives DDL from the model so that schema
        /// knowledge is never duplicated. Call after <see cref="DatabaseFacade.EnsureCreated"/>
        /// so that existing databases pick up any newly added tables or columns.
        /// </summary>
        public void EnsureSchemaUpdated()
        {
            // 1. Create any missing tables and indexes from the model DDL
            var script = Database.GenerateCreateScript();
            var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var statement in statements)
            {
                if (statement.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    Database.ExecuteSqlRaw(
                        statement.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS", StringComparison.OrdinalIgnoreCase));
                }
                else if (statement.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                {
                    Database.ExecuteSqlRaw(
                        statement.Replace("CREATE UNIQUE INDEX", "CREATE UNIQUE INDEX IF NOT EXISTS", StringComparison.OrdinalIgnoreCase));
                }
                else if (statement.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase))
                {
                    Database.ExecuteSqlRaw(
                        statement.Replace("CREATE INDEX", "CREATE INDEX IF NOT EXISTS", StringComparison.OrdinalIgnoreCase));
                }
                // Skip INSERT (seed data) and everything else
            }

            // 2. Add any missing columns to existing tables or any other element that is missing from the above automation
            // Execute migration queries
            var listOfQueries = new List<string>()
            {
                // Add any migration queries here
            };

            foreach (var item in listOfQueries)
            {
                Database.ExecuteSqlRaw(item);
            }
        }

        #region Audit logging

        /// <summary>
        /// Entity types that are tracked for audit logging.
        /// </summary>
        private static readonly HashSet<Type> AuditedTypes = new()
        {
            typeof(DBWS_Application),
            typeof(DBWS_Entity),
            typeof(DBWS_EntityProperty),
            typeof(DBWS_CustomEndpoint),
            typeof(DBWS_Collaborate),
            typeof(DBWS_ReportItem),
            typeof(DBWS_Server),
            typeof(GlobalSettings),
            typeof(IdentityUserRole<string>)
        };

        /// <summary>
        /// Properties whose values are masked in audit logs for security.
        /// </summary>
        private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "AdminAuthToken",
            "EncryptionKey", "ConnectionString", "MailPassword", "InstallationKey"
        };

        public override int SaveChanges()
        {
            try
            {
                CollectAuditEntries();
            }
            catch (Exception ex)
            {
                // Log and continue
                _logger.LogError(ex, $"Error collecting audit entries");
            }

            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await CollectAuditEntriesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Log and continue
                _logger.LogError(ex, $"Error collecting audit entries");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Inspects the ChangeTracker for auditable entity changes and adds
        /// <see cref="PortalAuditLog"/> entries to the same unit-of-work,
        /// ensuring they are persisted in the same transaction.
        /// </summary>
        private void CollectAuditEntries()
        {
            var auditEntries = BuildAuditEntries();

            foreach (var entry in auditEntries)
            {
                // For Modified entities, fetch the real DB values (sync)
                PropertyValues? dbValues = null;
                if (entry.EfEntry.State == EntityState.Modified)
                {
                    dbValues = entry.EfEntry.GetDatabaseValues();
                }

                AddAuditLog(entry, dbValues);
            }
        }

        private async Task CollectAuditEntriesAsync(CancellationToken cancellationToken)
        {
            var auditEntries = BuildAuditEntries();

            foreach (var entry in auditEntries)
            {
                // For Modified entities, fetch the real DB values (async)
                PropertyValues? dbValues = null;
                if (entry.EfEntry.State == EntityState.Modified)
                {
                    dbValues = await entry.EfEntry.GetDatabaseValuesAsync(cancellationToken);
                }

                AddAuditLog(entry, dbValues);
            }
        }

        /// <summary>
        /// Scans the ChangeTracker for auditable changes and returns
        /// the data needed to build audit log entries, or null when
        /// there is no authenticated user or nothing to audit.
        /// </summary>
        private IEnumerable<PendingAuditEntry> BuildAuditEntries()
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
            {
                return Enumerable.Empty<PendingAuditEntry>();
            }

            return ChangeTracker.Entries()
                .Where(e => e.Entity is not PortalAuditLog)
                .Where(e => AuditedTypes.Contains(e.Entity.GetType()))
                .Where(e => e.State == EntityState.Added
                         || e.State == EntityState.Modified
                         || e.State == EntityState.Deleted)
                .Select(e => new PendingAuditEntry
                {
                    EfEntry = e,
                    UserId = httpContext.User.Identity.GetUserId(),
                    UserEmail = httpContext.User.Identity.GetUserEmail(),
                    Timestamp = DateTime.UtcNow
                });
        }

        private void AddAuditLog(PendingAuditEntry pending, PropertyValues? dbValues)
        {
            var entry = pending.EfEntry;
            var changes = BuildChanges(entry, dbValues);

            // Skip if Modified but no properties actually changed value
            if (entry.State == EntityState.Modified && (changes == null || changes.Count == 0))
            {
                return;
            }

            AuditLogs.Add(new PortalAuditLog
            {
                Timestamp = pending.Timestamp,
                UserId = pending.UserId,
                UserEmail = pending.UserEmail,
                AppID = GetAppId(entry),
                EntityType = GetEntityTypeName(entry.Entity),
                EntityIdentifier = GetEntityIdentifier(entry),
                Action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Modified => "Modified",
                    EntityState.Deleted => "Deleted",
                    _ => entry.State.ToString()
                },
                Changes = changes != null && changes.Count > 0
                    ? JsonSerializer.Serialize(changes)
                    : null
            });
        }

        private static List<AuditPropertyChange>? BuildChanges(EntityEntry entry, PropertyValues? dbValues)
        {
            return entry.State switch
            {
                EntityState.Added => entry.Properties
                    .Where(p => !p.Metadata.IsPrimaryKey())
                    .Select(p => new AuditPropertyChange
                    {
                        Property = p.Metadata.Name,
                        OldValue = null,
                        NewValue = MaskIfSensitive(p.Metadata.Name, p.CurrentValue)
                    })
                    .ToList(),

                EntityState.Modified => entry.Properties
                    .Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
                    .Select(p => new AuditPropertyChange
                    {
                        Property = p.Metadata.Name,
                        OldValue = MaskIfSensitive(p.Metadata.Name, dbValues?[p.Metadata.Name]),
                        NewValue = MaskIfSensitive(p.Metadata.Name, p.CurrentValue)
                    })
                    .Where(c => !string.Equals(c.OldValue, c.NewValue, StringComparison.Ordinal))
                    .ToList(),

                EntityState.Deleted => entry.Properties
                    .Where(p => !p.Metadata.IsPrimaryKey())
                    .Select(p => new AuditPropertyChange
                    {
                        Property = p.Metadata.Name,
                        OldValue = MaskIfSensitive(p.Metadata.Name, p.OriginalValue),
                        NewValue = null
                    })
                    .ToList(),

                _ => null
            };
        }

        private long? GetAppId(EntityEntry entry)
        {
            var entity = entry.Entity;
            return entity switch
            {
                DBWS_Application app => app.ID > 0 ? app.ID : null,
                DBWS_Entity ent => ent.AppID > 0 ? ent.AppID : null,
                DBWS_EntityProperty prop => GetAppIdForProperty(prop),
                DBWS_CustomEndpoint ce => ce.AppID > 0 ? ce.AppID : null,
                DBWS_Collaborate collab => collab.AppID > 0 ? collab.AppID : null,
                DBWS_ReportItem report => report.AppID > 0 ? report.AppID : null,
                _ => null // Admin-level: Server, GlobalSettings, UserRole
            };
        }

        private long? GetAppIdForProperty(DBWS_EntityProperty prop)
        {
            // Try to resolve the parent Entity from the local cache (it is almost
            // always tracked by the time a property is being saved).
            var parentEntity = Entities.Local.FirstOrDefault(e => e.ID == prop.EntityID);
            if (parentEntity != null && parentEntity.AppID > 0)
            {
                return parentEntity.AppID;
            }

            return null;
        }

        private static string GetEntityTypeName(object entity)
        {
            return entity switch
            {
                DBWS_Application => "Application",
                DBWS_Entity => "Entity",
                DBWS_EntityProperty => "Property",
                DBWS_CustomEndpoint => "Custom Endpoint",
                DBWS_Collaborate => "Collaboration",
                DBWS_ReportItem => "Report",
                DBWS_Server => "Server",
                Common.Models.GlobalSettings => "Global Settings",
                IdentityUserRole<string> => "User Role",
                _ => entity.GetType().Name
            };
        }

        private string GetEntityIdentifier(EntityEntry entry)
        {
            var entity = entry.Entity;
            return entity switch
            {
                DBWS_Application app => app.Name ?? app.Token ?? "New Application",
                DBWS_Entity ent => ent.Name ?? "Unknown",
                DBWS_EntityProperty prop => prop.Name ?? "Unknown",
                DBWS_CustomEndpoint ce => ce.Name ?? "Unknown",
                DBWS_Collaborate collab => collab.UserEmail ?? "Unknown",
                DBWS_ReportItem report => report.Title ?? "Unknown",
                DBWS_Server server => server.Name ?? "Unknown",
                Apilane.Common.Models.GlobalSettings => "Instance Settings",
                IdentityUserRole<string> userRole => ResolveUserEmailForRole(userRole),
                _ => entry.Properties
                    .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? "Unknown"
            };
        }

        private string ResolveUserEmailForRole(IdentityUserRole<string> userRole)
        {
            var user = Users.Local.FirstOrDefault(u => u.Id == userRole.UserId);
            return user?.Email ?? $"User:{userRole.UserId}";
        }

        private static string? MaskIfSensitive(string propertyName, object? value)
        {
            if (value is null)
            {
                return null;
            }

            return SensitiveProperties.Contains(propertyName) ? "***" : value.ToString();
        }

        #endregion
    }

    internal class AuditPropertyChange
    {
        public string Property { get; set; } = null!;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    internal class PendingAuditEntry
    {
        public EntityEntry EfEntry { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string UserEmail { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    public class ApplicationUser : IdentityUser
    {
        public DateTime LastLogin { get; set; }
        public DateTime DateRegistered { get; set; }
        public string? AdminAuthToken { get; set; }
    }
}
