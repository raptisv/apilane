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

        // ─── GetEventsPlaceholdersAndDescriptions ─────────────────────────────────

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_ReturnsNonEmptyList()
        {
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count > 0);
        }

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_EachEventContainsUserProperties()
        {
            // Both events have IsUserTriggeredSameAsAcceptingTheEmail=true, so each contributes
            // exactly UserProperties.Count (3) user-property entries (no "From" entries).
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();
            var eventCount = EmailEvent.EmailEvents.Count;
            var userPropCount = EmailEvent.UserProperties.Count;

            // Count entries whose Name starts with "{Users." but NOT "{Users.From."
            var directUserEntries = result
                .Select(r => GetProperty(r, "Name"))
                .Where(n => n != null && n.StartsWith("{Users.") && !n.StartsWith("{Users.From."))
                .ToList();

            Assert.AreEqual(eventCount * userPropCount, directUserEntries.Count);
        }

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_NoFromEntriesWhenAllEventsAreSameUser()
        {
            // Every event in EmailEvents has IsUserTriggeredSameAsAcceptingTheEmail=true,
            // so "Users.From.*" properties should never be emitted.
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();
            var fromEntries = result
                .Select(r => GetProperty(r, "Name"))
                .Where(n => n != null && n.StartsWith("{Users.From."))
                .ToList();

            Assert.AreEqual(0, fromEntries.Count);
        }

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_ContainsConfirmationUrlEntry()
        {
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();
            var names = result.Select(r => GetProperty(r, "Name")).ToList();
            CollectionAssert.Contains(names, $"{{{EmailEventsPlaceholders.confirmation_url}}}");
        }

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_ContainsResetPasswordUrlEntry()
        {
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();
            var names = result.Select(r => GetProperty(r, "Name")).ToList();
            CollectionAssert.Contains(names, $"{{{EmailEventsPlaceholders.reset_password_url}}}");
        }

        [TestMethod]
        public void GetEventsPlaceholdersAndDescriptions_AllEntriesHaveEventCodeSet()
        {
            var validCodes = EmailEvent.EmailEvents.Select(e => e.Code.ToString()).ToHashSet();
            var result = EmailEvent.GetEventsPlaceholdersAndDescriptions();

            foreach (var entry in result)
            {
                var eventValue = GetProperty(entry, "Event");
                Assert.IsNotNull(eventValue, "Entry has null Event");
                Assert.IsTrue(validCodes.Contains(eventValue), $"Unknown event code: {eventValue}");
            }
        }

        // ─── Helper ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a named property from an anonymous object via reflection.
        /// </summary>
        private static string? GetProperty(object obj, string propertyName)
        {
            return obj.GetType().GetProperty(propertyName)?.GetValue(obj)?.ToString();
        }
    }
}
