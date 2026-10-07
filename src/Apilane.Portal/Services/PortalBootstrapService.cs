using Apilane.Common;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class PortalBootstrapService : IPortalBootstrapService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IPortalSettingsService _settingsService;

        public PortalBootstrapService(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IPortalSettingsService settingsService)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _signInManager = signInManager;
            _settingsService = settingsService;
        }

        public async Task<BootstrapCredential?> InitializeAsync()
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var state = await _dbContext.BootstrapStates.SingleOrDefaultAsync();

            if (state is null)
            {
                // Setup belongs only to newly seeded databases. An existing installation has
                // no setup row: preserve every account and credential exactly as it is.
                state = new PortalBootstrapState
                {
                    ID = 1,
                    Completed = true
                };
                _dbContext.BootstrapStates.Add(state);
            }

            BootstrapCredential? credential = null;

            if (!state.Completed)
            {
                // Each process start replaces a pending credential. An operator who lost the
                // previous output can restart safely; completed passwords are never rotated.
                var user = await _dbContext.Users.SingleAsync(x => x.Id == state.UserId);
                var temporaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

                user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, temporaryPassword);
                user.SecurityStamp = Guid.NewGuid().ToString();
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                user.AdminAuthToken = null;
                state.TemporaryPasswordIssued = true;
                credential = new BootstrapCredential(temporaryPassword);
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return credential;
        }

        public async Task<bool> IsRequiredAsync()
        {
            // Fail closed if initialization has not created the row.
            return !await _dbContext.BootstrapStates.AsNoTracking().AnyAsync(x => x.Completed);
        }

        public async Task<SessionResponse> CompleteAsync(BootstrapRequest request)
        {
            try
            {
                return await CompleteCoreAsync(request);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw PortalException.Conflict("Administrator setup changed during this request. Reload the page and try again.");
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                throw PortalException.Conflict("Administrator setup is being completed by another request. Reload the page and try again.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6)
            {
                throw PortalException.Conflict("Administrator setup is being completed by another request. Reload the page and try again.");
            }
        }

        private async Task<SessionResponse> CompleteCoreAsync(BootstrapRequest request)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var state = await _dbContext.BootstrapStates.SingleOrDefaultAsync();

            if (state is null || state.Completed)
            {
                throw PortalException.Conflict("Administrator setup has already been completed.");
            }

            var user = await _userManager.FindByIdAsync(state.UserId ?? string.Empty);

            if (user is null
                || !state.TemporaryPasswordIssued
                || !await _userManager.CheckPasswordAsync(user, request.TemporaryPassword))
            {
                throw PortalException.Unauthorized();
            }

            var email = request.Email.Trim();
            if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            {
                throw PortalException.Validation(nameof(BootstrapRequest.Email), AccountMessages.Email);
            }

            if (PortalAgent.IsAgent(email))
            {
                throw PortalException.Validation(nameof(BootstrapRequest.Email), "Addresses at agent.local are reserved for agents");
            }

            var normalizedEmail = _userManager.NormalizeEmail(email);
            var normalizedUserName = _userManager.NormalizeName(email);
            if (await _dbContext.Users.AnyAsync(x => x.Id != user.Id
                && (x.NormalizedEmail == normalizedEmail || x.NormalizedUserName == normalizedUserName)))
            {
                throw PortalException.Validation(nameof(BootstrapRequest.Email), "An account with this email already exists.");
            }

            if (string.Equals(request.Password, request.TemporaryPassword, StringComparison.Ordinal))
            {
                throw PortalException.Validation(nameof(BootstrapRequest.Password), "Choose a password different from the temporary password.");
            }

            // Identity validates the user and normalizes both identifiers when it saves the
            // password. All of them stay in the same transaction as the completed setup state.
            user.Email = email;
            user.UserName = email;
            user.EmailConfirmed = true;
            var result = await _userManager.ChangePasswordAsync(user, request.TemporaryPassword, request.Password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(x => x.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
                {
                    throw PortalException.Conflict("Administrator setup changed during this request. Reload the page and try again.");
                }

                throw PortalException.Validation(result.Errors.Select(x => new ErrorDetail
                {
                    Property = x.Code.StartsWith("Password", StringComparison.Ordinal)
                        ? nameof(BootstrapRequest.Password)
                        : nameof(BootstrapRequest.Email),
                    Message = x.Description
                }).ToList());
            }

            user.AdminAuthToken = null;
            state.Completed = true;
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            // No cookie or API-server token exists until the chosen email, password and setup
            // state are saved.
            await _signInManager.SignInAsync(user, isPersistent: false);
            return new SessionResponse
            {
                Email = user.Email ?? string.Empty,
                IsAdmin = (await _userManager.GetRolesAsync(user)).Contains(Globals.AdminRoleName),
                InstanceTitle = _settingsService.Get().InstanceTitle,
                Version = typeof(Program).Assembly.GetVersion()
            };
        }
    }
}
