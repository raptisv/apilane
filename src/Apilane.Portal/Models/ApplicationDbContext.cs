using Apilane.Common;
using Apilane.Common.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;

namespace Apilane.Portal.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<GlobalSettings> GlobalSettings { get; set; }
        public DbSet<DBWS_Server> Servers { get; set; }
        public DbSet<DBWS_Application> Applications { get; set; }
        public DbSet<DBWS_Entity> Entities { get; set; }
        public DbSet<DBWS_EntityProperty> EntityProperties { get; set; }
        public DbSet<DBWS_CustomEndpoint> CustomEndpoints { get; set; }
        public DbSet<DBWS_Collaborate> Collaborations { get; set; }
        public DbSet<DBWS_ReportItem> Reports { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {

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
    }

    public class ApplicationUser : IdentityUser
    {
        public DateTime LastLogin { get; set; }
        public DateTime DateRegistered { get; set; }
        public string? AdminAuthToken { get; set; }
    }
}