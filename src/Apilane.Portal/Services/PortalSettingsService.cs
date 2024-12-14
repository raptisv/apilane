using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using System.Linq;

namespace Apilane.Portal.Services
{
    public class PortalSettingsService : IPortalSettingsService
    {
        private readonly ApplicationDbContext _context;

        public PortalSettingsService(ApplicationDbContext context)
        {
            _context = context;
        }

        public GlobalSettings Get()
        {
            return _context.GlobalSettings.SingleOrDefault()
                ?? throw new System.Exception("No portal settings setup");
        }
    }
}
