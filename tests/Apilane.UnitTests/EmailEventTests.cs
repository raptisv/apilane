using Apilane.Common.Enums;
using Apilane.Common.Helpers;

namespace Apilane.UnitTests
{
    [TestClass]
    public class EmailEventTests
    {
        // ─── EmailEvents static list ─────────────────────────────────────────────

        [TestMethod]
        public void EmailEvents_ContainsTwoEvents()
        {
            Assert.AreEqual(2, EmailEvent.EmailEvents.Count);
        }

        [TestMethod]
        public void EmailEvents_ContainsUserRegisterConfirmation()
        {
            var codes = EmailEvent.EmailEvents.Select(e => e.Code).ToList();
            CollectionAssert.Contains(codes, EmailEventsCodes.UserRegisterConfirmation);
        }

        [TestMethod]
        public void EmailEvents_ContainsUserForgotPassword()
        {
            var codes = EmailEvent.EmailEvents.Select(e => e.Code).ToList();
            CollectionAssert.Contains(codes, EmailEventsCodes.UserForgotPassword);
        }

        [TestMethod]
        public void EmailEvents_UserRegisterConfirmation_IsSameUserTriggered()
        {
            var ev = EmailEvent.EmailEvents.First(e => e.Code == EmailEventsCodes.UserRegisterConfirmation);
            Assert.IsTrue(ev.IsUserTriggeredSameAsAcceptingTheEmail);
        }

        [TestMethod]
        public void EmailEvents_UserForgotPassword_IsSameUserTriggered()
        {
            var ev = EmailEvent.EmailEvents.First(e => e.Code == EmailEventsCodes.UserForgotPassword);
            Assert.IsTrue(ev.IsUserTriggeredSameAsAcceptingTheEmail);
        }

        [TestMethod]
        public void EmailEvents_UserRegisterConfirmation_HasConfirmationUrlPlaceholder()
        {
            var ev = EmailEvent.EmailEvents.First(e => e.Code == EmailEventsCodes.UserRegisterConfirmation);
            CollectionAssert.Contains(ev.Placeholders, EmailEventsPlaceholders.confirmation_url);
        }

        [TestMethod]
        public void EmailEvents_UserForgotPassword_HasResetPasswordUrlPlaceholder()
        {
            var ev = EmailEvent.EmailEvents.First(e => e.Code == EmailEventsCodes.UserForgotPassword);
            CollectionAssert.Contains(ev.Placeholders, EmailEventsPlaceholders.reset_password_url);
        }

        // ─── UserProperties static dictionary ────────────────────────────────────

        [TestMethod]
        public void UserProperties_ContainsThreeEntries()
        {
            Assert.AreEqual(3, EmailEvent.UserProperties.Count);
        }

        [TestMethod]
        public void UserProperties_ContainsID_Username_Email()
        {
            Assert.IsTrue(EmailEvent.UserProperties.ContainsKey("ID"));
            Assert.IsTrue(EmailEvent.UserProperties.ContainsKey("Username"));
            Assert.IsTrue(EmailEvent.UserProperties.ContainsKey("Email"));
        }
    }
}
