using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationWriteScope : IApplicationWriteScope
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IApiServerCacheReset _apiServerCacheReset;
        private readonly ILogger<ApplicationWriteScope> _logger;

        public ApplicationWriteScope(
            ApplicationDbContext dbContext,
            IApiServerCacheReset apiServerCacheReset,
            ILogger<ApplicationWriteScope> logger)
        {
            _dbContext = dbContext;
            _apiServerCacheReset = apiServerCacheReset;
            _logger = logger;
        }

        public async Task SaveAsync(DBWS_Application application, Func<Task>? callApiServer = null)
        {
            if (callApiServer is null)
            {
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                await callApiServer();

                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException exception)
                {
                    // The API server has the change now and the Portal does not. Nothing is undone
                    // here; the message tells the caller what state things are in.
                    _logger.LogError(exception, "A change was made on the API server but not saved in the Portal | Application {Token}", application.Token);

                    throw new PortalException(
                        StatusCodes.Status500InternalServerError,
                        PortalErrorCode.Error,
                        "The change was made on the API server but could not be saved in the Portal.");
                }
            }

            await _apiServerCacheReset.ResetAfterWriteAsync(application);
        }
    }
}
