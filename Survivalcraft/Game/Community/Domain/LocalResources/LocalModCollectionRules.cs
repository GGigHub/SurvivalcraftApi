using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    internal static class LocalModCollectionRules
    {
        public static LocalResourceItem[] OrderPendingMods(IEnumerable<LocalResourceItem> pendingMods)
        {
            return (pendingMods ?? [])
                .OrderByDescending(item => item?.CreationTime).ToArray();
        }

        public static void MarkDuplicatePackageNames(
            IEnumerable<LocalResourceItem> loadedMods,
            IEnumerable<LocalResourceItem> pendingMods)
        {
            LocalResourceItem[] allMods = (loadedMods ?? [])
                .Concat(pendingMods ?? [])
                .Where(item => item != null)
                .ToArray();
            foreach (LocalResourceItem item in allMods)
            {
                item.HasDuplicatePackageName = false;
            }
            foreach (IGrouping<string, LocalResourceItem> group in allMods
                .Where(item => !string.IsNullOrWhiteSpace(item.PackageName))
                .GroupBy(item => item.PackageName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                foreach (LocalResourceItem item in group)
                {
                    item.HasDuplicatePackageName = true;
                }
            }
        }
    }
}
