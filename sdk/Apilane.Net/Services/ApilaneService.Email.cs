using Apilane.Net.Abstractions;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Services
{
    public sealed partial class ApilaneService : IApilaneService
    {
        /// <summary>
        /// Confirms the email of a user with the token of the confirmation email (the value of the
        /// <c>{confirmation_token}</c> placeholder). Succeeds also for an email that is already confirmed.
        /// An unknown or expired token is a VALIDATION error for the property Token.
        /// </summary>
        public async Task<Either<bool, ApilaneError>> EmailConfirmEmailAsync(
            EmailConfirmEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            return await PostEmailTokenAsync(request, new { Token = request.Token }, cancellationToken);
        }

        /// <summary>
        /// Sets a new password with the token of the reset email (the value of the
        /// <c>{reset_password_token}</c> placeholder). The password has 8 to 100 characters. A successful reset
        /// ends all the sessions of the user and invalidates all the reset links. An unknown or expired token
        /// is a VALIDATION error for the property Token; a password that is too short or too long, for the
        /// property Password.
        /// </summary>
        public async Task<Either<bool, ApilaneError>> EmailResetPasswordAsync(
            EmailResetPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            return await PostEmailTokenAsync(request, new { Token = request.Token, Password = request.NewPassword }, cancellationToken);
        }

        private async Task<Either<bool, ApilaneError>> PostEmailTokenAsync<TBuilder>(
            ApilaneRequestBase<TBuilder> request,
            object body,
            CancellationToken cancellationToken) where TBuilder : ApilaneRequestBase<TBuilder>
        {
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.GetUrl(_config.ApplicationApiUrl)))
            {
                httpRequest.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await SendAsync(httpRequest, cancellationToken);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var jsonString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonSerializer.Deserialize<ApilaneError>(jsonString, JsonDeserializerSettings)!;
                    if (request.ShouldThrowExceptionOnError())
                    {
                        throw new ApilaneErrorException(errorResponse, (int)response.StatusCode);
                    }
                    return errorResponse;
                }

                return true;
            }
        }
    }
}
