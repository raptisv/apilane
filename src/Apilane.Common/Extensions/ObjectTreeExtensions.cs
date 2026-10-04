using System.Collections.Generic;
using System.Linq;

namespace Apilane.Common.Extensions
{
    public static class ObjectTreeExtensions
    {
        public class GroupItem
        {
            /// <summary>
            /// Default level is int max
            /// </summary>
            public int Level { get; set; } = int.MaxValue;
            public string ID { get; set; } = null!;
            public string? ParentID { get; set; }
            public List<GroupItem> Children { get; set; } = new List<GroupItem>();
        }

        public static IList<GroupItem> BuildTree(this IEnumerable<GroupItem> source)
        {
            var groups = source.GroupBy(i => i.ParentID);

            var roots = groups.FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key))?.ToList();

            if (roots is null)
            {
                return new List<GroupItem>();
            }

            if (roots.Count > 0)
            {
                var dict = groups.Where(g => !string.IsNullOrWhiteSpace(g.Key)).ToDictionary(g => g.Key!, g => g.ToList());
                var path = new HashSet<GroupItem>();
                for (int i = 0; i < roots.Count; i++)
                    AddChildren(roots[i], dict, 1, path);
            }

            return roots;
        }

        private static void AddChildren(GroupItem node, IDictionary<string, List<GroupItem>> source, int level, HashSet<GroupItem> path)
        {
            // A foreign-key cycle (an entity pointing to itself, or two pointing to each other)
            // would otherwise recurse until the stack overflows. Only the current path is
            // remembered: reaching a node again through a longer path is what raises its level.
            if (!path.Add(node))
            {
                return;
            }

            if (source.ContainsKey(node.ID))
            {
                node.Children = source[node.ID];
                // Prevent re-setting a higher level.
                if (node.Level == int.MaxValue || node.Level < level)
                {
                    node.Level = level;
                }
                for (int i = 0; i < node.Children.Count; i++)
                    AddChildren(node.Children[i], source, (level + 1), path);
            }
            else
            {
                node.Children = new List<GroupItem>();
            }

            path.Remove(node);
        }
    }
}
