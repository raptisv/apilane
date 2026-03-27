using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class DBWS_EntityPropertyTests
    {
        private static DBWS_EntityProperty MakeProperty(
            PropertyType type = PropertyType.String,
            bool isPrimaryKey = false,
            bool isSystem = false,
            bool required = false,
            bool encrypted = false,
            string? validationRegex = null,
            int? decimalPlaces = null,
            long? minimum = null,
            long? maximum = null,
            string name = "TestProp")
        {
            return new DBWS_EntityProperty
            {
                ID = 1,
                EntityID = 1,
                Name = name,
                TypeID = (int)type,
                IsPrimaryKey = isPrimaryKey,
                IsSystem = isSystem,
                Required = required,
                Encrypted = encrypted,
                ValidationRegex = validationRegex,
                DecimalPlaces = decimalPlaces,
                Minimum = minimum,
                Maximum = maximum,
                DateModified = DateTime.UtcNow
            };
        }

        // --- Descr() ---

        [TestMethod]
        public void Descr_PrimaryKey_ReturnsEmptyList()
        {
            var prop = MakeProperty(isPrimaryKey: true, required: true, type: PropertyType.Number);
            var result = prop.Descr();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Descr_Required_IncludesRequiredEntry()
        {
            var prop = MakeProperty(required: true, type: PropertyType.String);
            var result = prop.Descr();
            Assert.IsTrue(result.Contains("Required"));
        }

        [TestMethod]
        public void Descr_NotRequired_DoesNotIncludeRequiredEntry()
        {
            var prop = MakeProperty(required: false, type: PropertyType.String);
            var result = prop.Descr();
            Assert.IsFalse(result.Contains("Required"));
        }

        [TestMethod]
        public void Descr_NumberWithDecimalPlaces_IncludesDecimalEntry()
        {
            var prop = MakeProperty(type: PropertyType.Number, decimalPlaces: 2);
            var result = prop.Descr();
            Assert.IsTrue(result.Any(x => x.Contains("Decimal places: 2")));
        }

        [TestMethod]
        public void Descr_StringWithDecimalPlaces_DoesNotIncludeDecimalEntry()
        {
            // AllowDecimalPlaces() returns false for String
            var prop = MakeProperty(type: PropertyType.String, decimalPlaces: 2);
            var result = prop.Descr();
            Assert.IsFalse(result.Any(x => x.Contains("Decimal places")));
        }

        [TestMethod]
        public void Descr_EncryptedString_IncludesEncryptedEntry()
        {
            var prop = MakeProperty(type: PropertyType.String, encrypted: true);
            var result = prop.Descr();
            Assert.IsTrue(result.Contains("Encrypted"));
        }

        [TestMethod]
        public void Descr_EncryptedNumber_DoesNotIncludeEncryptedEntry()
        {
            // AllowEncrypted() returns false for Number
            var prop = MakeProperty(type: PropertyType.Number, encrypted: true);
            var result = prop.Descr();
            Assert.IsFalse(result.Contains("Encrypted"));
        }

        [TestMethod]
        public void Descr_StringWithRegex_IncludesRegexEntry()
        {
            var prop = MakeProperty(type: PropertyType.String, validationRegex: @"\d+");
            var result = prop.Descr();
            Assert.IsTrue(result.Any(x => x.Contains(@"Regex: \d+")));
        }

        [TestMethod]
        public void Descr_NumberWithRegex_DoesNotIncludeRegexEntry()
        {
            var prop = MakeProperty(type: PropertyType.Number, validationRegex: @"\d+");
            var result = prop.Descr();
            Assert.IsFalse(result.Any(x => x.Contains("Regex")));
        }

        [TestMethod]
        public void Descr_StringWithMinAndMax_IncludesMinMaxEntries()
        {
            var prop = MakeProperty(type: PropertyType.String, minimum: 5, maximum: 100);
            var result = prop.Descr();
            Assert.IsTrue(result.Any(x => x.Contains("Min: 5")));
            Assert.IsTrue(result.Any(x => x.Contains("Max: 100")));
        }

        [TestMethod]
        public void Descr_BooleanType_NeverIncludesMinMaxOrEncryptedOrRegex()
        {
            var prop = MakeProperty(type: PropertyType.Boolean, minimum: 0, maximum: 1, encrypted: true, validationRegex: "x");
            var result = prop.Descr();
            Assert.IsFalse(result.Any(x => x.StartsWith("Min")));
            Assert.IsFalse(result.Any(x => x.StartsWith("Max")));
            Assert.IsFalse(result.Contains("Encrypted"));
            Assert.IsFalse(result.Any(x => x.StartsWith("Regex")));
        }

        // --- IsOnUTC() ---

        [TestMethod]
        public void IsOnUTC_SystemDateCreated_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.Date, isSystem: true, name: Globals.CreatedColumn);
            Assert.IsTrue(prop.IsOnUTC());
        }

        [TestMethod]
        public void IsOnUTC_SystemDateLastLogin_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.Date, isSystem: true, name: "LastLogin");
            Assert.IsTrue(prop.IsOnUTC());
        }

        [TestMethod]
        public void IsOnUTC_NonSystemDateCreated_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Date, isSystem: false, name: Globals.CreatedColumn);
            Assert.IsFalse(prop.IsOnUTC());
        }

        [TestMethod]
        public void IsOnUTC_SystemStringCreated_ReturnsFalse()
        {
            // Type is not Date
            var prop = MakeProperty(type: PropertyType.String, isSystem: true, name: Globals.CreatedColumn);
            Assert.IsFalse(prop.IsOnUTC());
        }

        [TestMethod]
        public void IsOnUTC_SystemDateOtherName_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Date, isSystem: true, name: "SomeOtherDate");
            Assert.IsFalse(prop.IsOnUTC());
        }

        // --- AllowEdit() ---

        [TestMethod]
        public void AllowEdit_PrimaryKey_ReturnsFalse()
        {
            var prop = MakeProperty(isPrimaryKey: true);
            Assert.IsFalse(prop.AllowEdit(null, false));
        }

        [TestMethod]
        public void AllowEdit_SystemOwnerColumn_ReturnsFalse()
        {
            var prop = MakeProperty(isSystem: true, name: Globals.OwnerColumn);
            Assert.IsFalse(prop.AllowEdit(null, false));
        }

        [TestMethod]
        public void AllowEdit_SystemCreatedColumn_ReturnsFalse()
        {
            var prop = MakeProperty(isSystem: true, name: Globals.CreatedColumn);
            Assert.IsFalse(prop.AllowEdit(null, false));
        }

        [TestMethod]
        public void AllowEdit_SystemEmailConfirmed_ReturnsFalse()
        {
            var prop = MakeProperty(isSystem: true, name: "EmailConfirmed");
            Assert.IsFalse(prop.AllowEdit(null, false));
        }

        [TestMethod]
        public void AllowEdit_SystemLastLogin_ReturnsFalse()
        {
            var prop = MakeProperty(isSystem: true, name: "LastLogin");
            Assert.IsFalse(prop.AllowEdit(null, false));
        }

        [TestMethod]
        public void AllowEdit_DifferentiationProperty_ReturnsFalse()
        {
            // The diff property name is "{entity}_ID"
            var prop = MakeProperty(name: "Tenant_ID");
            Assert.IsFalse(prop.AllowEdit("Tenant", entityHasDifferentiationProperty: true));
        }

        [TestMethod]
        public void AllowEdit_DifferentiationPropertyButEntityHasNone_ReturnsTrue()
        {
            var prop = MakeProperty(name: "Tenant_ID");
            Assert.IsTrue(prop.AllowEdit("Tenant", entityHasDifferentiationProperty: false));
        }

        [TestMethod]
        public void AllowEdit_RegularUserProperty_ReturnsTrue()
        {
            var prop = MakeProperty(isSystem: false, name: "MyField");
            Assert.IsTrue(prop.AllowEdit(null, false));
        }

        // --- AllowMin / AllowMax / AllowMaxEdit / AllowDecimalPlaces / AllowValidationRegex / AllowEncrypted ---

        [TestMethod]
        public void AllowMin_NumberNonPK_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.Number, isPrimaryKey: false);
            Assert.IsTrue(prop.AllowMin());
        }

        [TestMethod]
        public void AllowMin_PrimaryKey_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Number, isPrimaryKey: true);
            Assert.IsFalse(prop.AllowMin());
        }

        [TestMethod]
        public void AllowMin_BooleanNonPK_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Boolean, isPrimaryKey: false);
            Assert.IsFalse(prop.AllowMin());
        }

        [TestMethod]
        public void AllowMaxEdit_NumberNonPK_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.Number, isPrimaryKey: false);
            Assert.IsTrue(prop.AllowMaxEdit());
        }

        [TestMethod]
        public void AllowMaxEdit_StringNonPK_ReturnsFalse()
        {
            // MaxEdit is only for Number
            var prop = MakeProperty(type: PropertyType.String, isPrimaryKey: false);
            Assert.IsFalse(prop.AllowMaxEdit());
        }

        [TestMethod]
        public void AllowDecimalPlaces_Number_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.Number);
            Assert.IsTrue(prop.AllowDecimalPlaces());
        }

        [TestMethod]
        public void AllowDecimalPlaces_String_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.String);
            Assert.IsFalse(prop.AllowDecimalPlaces());
        }

        [TestMethod]
        public void AllowValidationRegex_String_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.String);
            Assert.IsTrue(prop.AllowValidationRegex());
        }

        [TestMethod]
        public void AllowValidationRegex_Number_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Number);
            Assert.IsFalse(prop.AllowValidationRegex());
        }

        [TestMethod]
        public void AllowEncrypted_String_ReturnsTrue()
        {
            var prop = MakeProperty(type: PropertyType.String);
            Assert.IsTrue(prop.AllowEncrypted());
        }

        [TestMethod]
        public void AllowEncrypted_Boolean_ReturnsFalse()
        {
            var prop = MakeProperty(type: PropertyType.Boolean);
            Assert.IsFalse(prop.AllowEncrypted());
        }

        // --- Clone() ---

        [TestMethod]
        public void Clone_ProducesIndependentCopy()
        {
            var original = MakeProperty(type: PropertyType.String, name: "OriginalProp", required: true, minimum: 1, maximum: 50);
            var clone = (DBWS_EntityProperty)original.Clone();

            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreEqual(original.TypeID, clone.TypeID);
            Assert.AreEqual(original.Required, clone.Required);
            Assert.AreEqual(original.Minimum, clone.Minimum);
            Assert.AreEqual(original.Maximum, clone.Maximum);
            Assert.AreNotSame(original, clone);
        }

        [TestMethod]
        public void Clone_ModifyingCloneDoesNotAffectOriginal()
        {
            var original = MakeProperty(name: "OriginalProp");
            var clone = (DBWS_EntityProperty)original.Clone();
            clone.Name = "ModifiedProp";

            Assert.AreEqual("OriginalProp", original.Name);
            Assert.AreEqual("ModifiedProp", clone.Name);
        }
    }
}
