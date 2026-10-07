using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using Xunit;

namespace Apilane.Portal.Tests
{
    public class PortalLinkBuilderTests
    {
        [Fact]
        public void ResetPassword_ConfiguredOrigin_Should_Encode_The_Code_Without_A_Request()
        {
            var links = new PortalLinkBuilder(Configuration("http://0.0.0.0:5000", "https://portal.example:8443"));

            Assert.Equal("https://portal.example:8443/account/reset-password?code=a%2Bb%2F%3D%26c", links.ResetPassword("a+b/=&c"));
            Assert.Equal("https://portal.example:8443/apps", links.Ui("/apps"));
        }

        [Fact]
        public void Ui_ConcreteListenUrl_Should_Be_The_Development_Fallback()
        {
            var links = new PortalLinkBuilder(Configuration("http://localhost:5000", null));

            Assert.Equal("http://localhost:5000/account/forgot-password", links.ForgotPassword());
        }

        [Theory]
        [InlineData("http://0.0.0.0:5000")]
        [InlineData("http://[::]:5000")]
        [InlineData("http://*:5000")]
        [InlineData("http://+:5000")]
        public void Ui_WildcardListenUrlWithoutPublicUrl_Should_Refuse_To_Build_Mail_Links(string listenUrl)
        {
            var links = new PortalLinkBuilder(Configuration(listenUrl, null));

            var error = Assert.Throws<PortalException>(() => links.ResetPassword("secret"));

            Assert.Contains("PublicUrl", error.Message);
            Assert.DoesNotContain("secret", error.Message);
        }

        [Theory]
        [InlineData("//portal.example")]
        [InlineData("ftp://portal.example")]
        [InlineData("https://user:password@portal.example")]
        [InlineData("https://portal.example/subpath")]
        [InlineData("https://portal.example/?query=value")]
        [InlineData("https://portal.example/#fragment")]
        [InlineData("http://0.0.0.0:5000")]
        public void Configuration_InvalidPublicUrl_Should_Fail_Early(string publicUrl)
        {
            Assert.Throws<ArgumentException>(() => Configuration("http://localhost:5000", publicUrl));
        }

        private static PortalConfiguration Configuration(string listenUrl, string? publicUrl)
        {
            return new PortalConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Url"] = listenUrl,
                ["PublicUrl"] = publicUrl,
                ["FilesPath"] = ".",
                ["InstanceTitle"] = "Tests",
                ["InstallationKey"] = "test-only-installation-key-value",
                ["ApiUrl"] = "http://api.invalid"
            }).Build());
        }
    }
}
