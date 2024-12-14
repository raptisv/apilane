using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Apilane.Api.Component.Tests.Infrastructure
{
    internal class WithSecurityAccess : IDisposable
    {
        protected ApiConfiguration ApiConfiguration;
        private IApplicationService _applicationServiceMock;
        private DBWS_Application _testApplication;
        private DBWS_Security _security;

        public WithSecurityAccess(
            ApiConfiguration apiConfiguration,
            IApplicationService applicationServiceMock,
            DBWS_Application testApplication,
            string entityOrEndpointName,
            string inRole = Globals.ANONYMOUS,
            SecurityTypes type = SecurityTypes.Entity,
            SecurityActionType actionType = SecurityActionType.get,
            EndpointRecordAuthorization recordAccess = EndpointRecordAuthorization.All,
            List<string>? properties = null,
            DBWS_Security.RateLimitItem? rateLimit = null)
        {
            ApiConfiguration = apiConfiguration;
            _applicationServiceMock = applicationServiceMock;
            _testApplication = testApplication;

            _security = new DBWS_Security()
            {
                Name = entityOrEndpointName,
                RoleID = inRole,
                TypeID = (int)type,
                Action = actionType.ToString().ToLower(),
                Record = (int)recordAccess,
                Properties = properties is null ? null : string.Join(",", properties),
                RateLimit = rateLimit
            };

            var currentSecurity = _testApplication.Security_List;
            currentSecurity.Add(_security);
            _testApplication.Security = JsonSerializer.Serialize(currentSecurity);

            MockApplicationService(_testApplication);
        }

        public void Dispose()
        {
            var currentSecurity = _testApplication.Security_List;
            currentSecurity.RemoveAll(x => 
            x.Name.Equals(_security.Name) && 
            x.Action == _security.Action.ToString().ToLower() &&
            x.RoleID == _security.RoleID &&
            x.TypeID == _security.TypeID);

            _testApplication.Security = JsonSerializer.Serialize(currentSecurity);

            MockApplicationService(_testApplication);
        }

        private void MockApplicationService(DBWS_Application application)
        {
            A.CallTo(() => _applicationServiceMock.GetAsync(application.Token))
                    .Returns(application);

            A.CallTo(() => _applicationServiceMock.GetDbInfoAsync(application.Token))
                .Returns(application.ToDbInfo(ApiConfiguration.FilesPath));
        }
    }
}
