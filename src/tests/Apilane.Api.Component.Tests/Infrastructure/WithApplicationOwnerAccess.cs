using Apilane.Api.Abstractions;
using FakeItEasy;
using System;

namespace Apilane.Api.Component.Tests.Infrastructure
{
    internal class WithApplicationOwnerAccess : IDisposable
    {
        private string _appToken;
        private IPortalInfoService _portalInfoServiceMock;

        public WithApplicationOwnerAccess(
            string appToken, 
            IPortalInfoService portalInfoServiceMock)
        {
            _appToken = appToken;
            _portalInfoServiceMock = portalInfoServiceMock;

            // Set the user as global admin to prevent validations
            A.CallTo(() => _portalInfoServiceMock.UserOwnsApplicationAsync(A<string>.Ignored, _appToken))
                .Returns(true);
        }

        public void Dispose()
        {
            // Set the user as NOT global admin to perform validations as needed
            A.CallTo(() => _portalInfoServiceMock.UserOwnsApplicationAsync(A<string>.Ignored, _appToken))
                .Returns(false);
        }
    }
}
