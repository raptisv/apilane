using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common.Enums;
using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IAccountAPI
    {
        Task<bool> ChangePasswordAsync(DBWS_Application application, Users currentUser, string currentPassword, string newPassword);
        Task<string?> ConfirmAsync(string appToken, string confirmationToken, string appName, string? emailConfirmationRedirectUrl);
        Task ConfirmEmailWithTokenAsync(DBWS_Application application, string? confirmationToken);
        Task ResetPasswordWithTokenAsync(DBWS_Application application, string? resetToken, string? newPassword);
        Task<UserDataDto> GetUserDataAsync(string appToken, Users currentUser, List<DBWS_Security> appSecurityList);
        Task<LoginResponseDto> LoginAsync(DBWS_Application application, DBWS_Entity usersEntity, string? username, string? email, string password);
        Task<long> LogoutEverywhereAsync(DBWS_Application application, long userId);

        /// <summary>
        /// Ends every session of the given users: deletes their tokens and resets the cache of each one, so that no
        /// token of theirs authenticates after the call returns.
        /// </summary>
        Task<long> LogoutEverywhereAsync(string appToken, IReadOnlyCollection<long> userIds);

        /// <summary>
        /// Deletes users with <paramref name="deleteUsersAsync"/>, which answers the IDs it deleted, and ends the
        /// sessions of those users before returning them. A delete that fails ends no session.
        /// </summary>
        Task<List<long>> DeleteUsersAndEndSessionsAsync(string appToken, IReadOnlyCollection<long> userIds, Func<Task<List<long>>> deleteUsersAsync);

        Task<bool> ResetPasswordAsync(DBWS_Application application, long userId, string newPassword);
        Task<long> RegisterAsync(string appToken, DBWS_Entity usersEntity, DatabaseType databaseType, string applicationServerUrl, EmailSettings? emailSettings, string appEncryptionKey, string? differentiationEntity, JsonObject userJObject, bool allowUserRegister);
        Task<LoginResponseDto> RenewAuthTokenAsync(string appToken, Users currentUser);
        Task<Dictionary<string, object?>> UpdateAsync(string appToken, DBWS_Entity usersEntity, Users currentUser, DatabaseType databaseType, string appEncryptionKey, string? differentiationEntity, JsonObject userJObject);
    }
}
