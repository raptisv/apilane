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

        [TestMethod]
        public void BuildTree_SelfReference_Should_Terminate()
        {
            // An entity with a foreign key to Users and one to itself.
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Users", ParentID = null },
                new GroupItem { ID = "Comments", ParentID = "Users" },
                new GroupItem { ID = "Comments", ParentID = "Comments" }
            };

            var result = items.BuildTree();

            Assert.AreEqual(1, result.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, items.Select(x => x.Level).ToArray());
        }

        [TestMethod]
        public void BuildTree_Cycle_Should_Terminate()
        {
            // Two entities that point to each other, one of them to Users too.
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Users", ParentID = null },
                new GroupItem { ID = "Alpha", ParentID = "Users" },
                new GroupItem { ID = "Alpha", ParentID = "Bravo" },
                new GroupItem { ID = "Bravo", ParentID = "Alpha" }
            };

            var result = items.BuildTree();

            Assert.AreEqual(1, result.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 3 }, items.Select(x => x.Level).ToArray());
        }

        [TestMethod]
        public void BuildTree_NodeReachedThroughTwoPaths_Should_Keep_The_Deepest_Level()
        {
            // Comments is reached through Tasks twice: from Users directly (level 3) and through
            // Projects (level 4). The second visit must not be skipped: the longer path counts.
            var items = new List<GroupItem>
            {
                new GroupItem { ID = "Users", ParentID = null },
                new GroupItem { ID = "Tasks", ParentID = "Users" },
                new GroupItem { ID = "Projects", ParentID = "Users" },
                new GroupItem { ID = "Tasks", ParentID = "Projects" },
                new GroupItem { ID = "Comments", ParentID = "Tasks" },
                new GroupItem { ID = "Replies", ParentID = "Comments" }
            };

            items.BuildTree();

            CollectionAssert.AreEqual(new[] { 1, 2, 2, 3, 4, int.MaxValue }, items.Select(x => x.Level).ToArray());
        }
    }
}
