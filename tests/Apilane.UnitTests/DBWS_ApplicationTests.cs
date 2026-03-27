using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_ApplicationTests
    {
        private static DBWS_Application BuildFullApp() => new DBWS_Application
        {
            Token = "tok",
            UserID = "uid",
            Name = "TestApp",
            ServerID = 1,
            Server = new DBWS_Server(),
            EncryptionKey = "key",
            AuthTokenExpireMinutes = 60,
            DatabaseType = 1,
            MailServer = "smtp.example.com",
            MailServerPort = 587,
            MailFromAddress = "from@example.com",
            MailFromDisplayName = "Test",
            MailUserName = "user@example.com",
            MailPassword = "secret",
            Entities = new System.Collections.Generic.List<DBWS_Entity>(),
            Reports = new System.Collections.Generic.List<DBWS_ReportItem>(),
            CustomEndpoints = new System.Collections.Generic.List<DBWS_CustomEndpoint>(),
            Collaborates = new System.Collections.Generic.List<DBWS_Collaborate>()
        };

        // ─── GetEmailSettings ───────────────────────────────────────────────────

        [TestMethod]
        public void GetEmailSettings_AllFieldsSet_ReturnsEmailSettings()
        {
            var app = BuildFullApp();
            var settings = app.GetEmailSettings();
            Assert.IsNotNull(settings);
            Assert.AreEqual("smtp.example.com", settings.MailServer);
            Assert.AreEqual(587, settings.MailServerPort);
            Assert.AreEqual("from@example.com", settings.MailFromAddress);
            Assert.AreEqual("Test", settings.MailFromDisplayName);
            Assert.AreEqual("user@example.com", settings.MailUserName);
            Assert.AreEqual("secret", settings.MailPassword);
        }

        [TestMethod]
        public void GetEmailSettings_NullMailServer_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailServer = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_EmptyMailServer_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailServer = string.Empty;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_NullMailServerPort_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailServerPort = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_ZeroMailServerPort_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailServerPort = 0;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_NullMailFromAddress_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailFromAddress = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_NullMailFromDisplayName_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailFromDisplayName = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_NullMailUserName_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailUserName = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        [TestMethod]
        public void GetEmailSettings_NullMailPassword_ReturnsNull()
        {
            var app = BuildFullApp();
            app.MailPassword = null;
            Assert.IsNull(app.GetEmailSettings());
        }

        // ─── Security_List ──────────────────────────────────────────────────────

        [TestMethod]
        public void SecurityList_NullSecurity_ReturnsEmptyList()
        {
            var app = BuildFullApp();
            app.Security = null;
            var list = app.Security_List;
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
        }

        [TestMethod]
        public void SecurityList_EmptySecurity_ReturnsEmptyList()
        {
            var app = BuildFullApp();
            app.Security = string.Empty;
            var list = app.Security_List;
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);
        }

        [TestMethod]
        public void SecurityList_ValidJson_DeserializesList()
        {
            var app = BuildFullApp();
            app.Security = "[{\"TypeID\":1,\"EntityID\":1,\"Properties\":\"*\",\"Record\":1,\"Action\":\"get\",\"RateLimit\":null,\"RateLimitWindowType\":0}]";
            var list = app.Security_List;
            Assert.IsNotNull(list);
            Assert.AreEqual(1, list.Count);
        }
    }
}
