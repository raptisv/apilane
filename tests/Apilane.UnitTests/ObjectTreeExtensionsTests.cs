using Apilane.Common.Extensions;
using static Apilane.Common.Extensions.ObjectTreeExtensions;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ObjectTreeExtensionsTests
    {
        [TestMethod]
        public void BuildTree_EmptySource_ReturnsEmptyList()
        {
            var items = new List<GroupItem>();

            var result = items.BuildTree();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildTree_NoRootItems_ReturnsEmptyList()
        {
            // All items have a ParentID — no root exists
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Child", ParentID = "NonExistentParent" }
            };

            var result = items.BuildTree();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildTree_SingleRootNoChildren_ReturnsSingleRoot()
        {
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Root", ParentID = null }
            };

            var result = items.BuildTree();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Root", result[0].ID);
            Assert.AreEqual(0, result[0].Children.Count);
        }

        [TestMethod]
        public void BuildTree_RootWithChildren_AttachesChildren()
        {
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Root", ParentID = null },
                new GroupItem { ID = "Child1", ParentID = "Root" },
                new GroupItem { ID = "Child2", ParentID = "Root" }
            };

            var result = items.BuildTree();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result[0].Children.Count);
        }

        [TestMethod]
        public void BuildTree_NestedHierarchy_AssignsCorrectLevels()
        {
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Root",  ParentID = null },
                new GroupItem { ID = "Mid",   ParentID = "Root" },
                new GroupItem { ID = "Leaf",  ParentID = "Mid" }
            };

            var result = items.BuildTree();

            var root = result[0];
            Assert.AreEqual(1, root.Children.Count);

            var mid = root.Children[0];
            Assert.AreEqual("Mid", mid.ID);
            Assert.AreEqual(1, mid.Children.Count);

            var leaf = mid.Children[0];
            Assert.AreEqual("Leaf", leaf.ID);
        }

        [TestMethod]
        public void BuildTree_MultipleRoots_ReturnsBothRoots()
        {
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Root1", ParentID = null },
                new GroupItem { ID = "Root2", ParentID = null }
            };

            var result = items.BuildTree();

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void BuildTree_DeepChain_BuildsCorrectly()
        {
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "A", ParentID = null },
                new GroupItem { ID = "B", ParentID = "A" },
                new GroupItem { ID = "C", ParentID = "B" },
                new GroupItem { ID = "D", ParentID = "C" }
            };

            var result = items.BuildTree();

            Assert.AreEqual(1, result.Count);
            var a = result[0];
            Assert.AreEqual("A", a.ID);
            Assert.AreEqual("B", a.Children[0].ID);
            Assert.AreEqual("C", a.Children[0].Children[0].ID);
            Assert.AreEqual("D", a.Children[0].Children[0].Children[0].ID);
        }
    }
}
