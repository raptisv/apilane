using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;

namespace Apilane.UnitTests
{
    [TestClass]
    public class FilterDataExtensionsTests
    {
        private static FilterData Leaf(string property) =>
            new(property, FilterData.FilterOperators.equal, "x", PropertyType.String);

        [TestMethod]
        public void ReferencesProperty_NullFilter_ReturnsFalse()
        {
            FilterData? filter = null;

            Assert.IsFalse(filter.ReferencesProperty("Secret"));
        }

        [TestMethod]
        public void ReferencesProperty_LeafMatchIsCaseInsensitive()
        {
            var filter = Leaf("Secret");

            Assert.IsTrue(filter.ReferencesProperty("secret"));
            Assert.IsTrue(filter.ReferencesProperty("SECRET"));
            Assert.IsFalse(filter.ReferencesProperty("Public"));
        }

        [TestMethod]
        public void ReferencesProperty_ForbiddenFilterFollowedByAllowedSibling_IsDetected()
        {
            // Regression: the old check reset its result on every sibling, so a forbidden
            // property was only caught when it was the last child of the group.
            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>
            {
                Leaf("Secret"),
                Leaf("Public")
            });

            Assert.IsTrue(filter.ReferencesProperty("Secret"));
        }

        [TestMethod]
        public void ReferencesProperty_ForbiddenFilterAsLastSibling_IsDetected()
        {
            var filter = new FilterData(FilterData.FilterLogic.OR, new List<FilterData>
            {
                Leaf("Public"),
                Leaf("Secret")
            });

            Assert.IsTrue(filter.ReferencesProperty("Secret"));
        }

        [TestMethod]
        public void ReferencesProperty_NestedTwoLevelsDeepFollowedBySiblings_IsDetected()
        {
            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>
            {
                new FilterData(FilterData.FilterLogic.OR, new List<FilterData>
                {
                    new FilterData(FilterData.FilterLogic.AND, new List<FilterData> { Leaf("Secret"), Leaf("Public") }),
                    Leaf("Public")
                }),
                Leaf("Public"),
                Leaf("Public")
            });

            Assert.IsTrue(filter.ReferencesProperty("Secret"));
        }

        [TestMethod]
        public void ReferencesProperty_NoReference_ReturnsFalse()
        {
            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>
            {
                Leaf("Public"),
                new FilterData(FilterData.FilterLogic.OR, new List<FilterData> { Leaf("Public"), Leaf("Other") })
            });

            Assert.IsFalse(filter.ReferencesProperty("Secret"));
        }
    }
}
