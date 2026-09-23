using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Engine;

namespace Game
{
    /// <summary>
    /// Captures the game-managed resources used to decide whether an integration
    /// pack item is already present. The cache is invalidated by import and scan
    /// completion events in the presentation layer.
    /// </summary>
    internal sealed class ModpackPresenceManager
    {
        private readonly Lock m_sync = new();
        private ModpackPresenceSnapshot m_cachedSnapshot;

        public ModpackPresenceSnapshot Capture(IEnumerable<LocalResourceItem> pendingMods)
        {
            lock (m_sync)
            {
                if (m_cachedSnapshot != null)
                {
                    return m_cachedSnapshot;
                }
            }

            ModpackPresenceSnapshot snapshot = new();
            foreach (WorldInfo worldInfo in WorldsManager.WorldInfos)
            {
                snapshot.WorldKeys.Add(ModpackModMatcher.NormalizeMatchKey(worldInfo?.WorldSettings?.Name));
                snapshot.WorldKeys.Add(ModpackModMatcher.NormalizeMatchKey(worldInfo?.DirectoryName));
                AddLocalFileName(snapshot, worldInfo?.DirectoryName);
            }

            foreach (string name in FurniturePacksManager.FurniturePackNames)
            {
                string fileName = FurniturePacksManager.GetFileName(name);
                snapshot.FurnitureKeys.Add(ModpackModMatcher.NormalizeMatchKey(name));
                snapshot.FurnitureKeys.Add(ModpackModMatcher.NormalizeMatchKey(fileName));
                AddLocalFileName(snapshot, fileName);
            }

            foreach (string name in BlocksTexturesManager.BlockTexturesNames)
            {
                string fileName = BlocksTexturesManager.GetFileName(name);
                snapshot.TextureKeys.Add(ModpackModMatcher.NormalizeMatchKey(name));
                snapshot.TextureKeys.Add(ModpackModMatcher.NormalizeMatchKey(fileName));
                AddLocalFileName(snapshot, fileName);
            }

            foreach (string name in CharacterSkinsManager.CharacterSkinsNames)
            {
                string fileName = CharacterSkinsManager.GetFileName(name);
                snapshot.SkinKeys.Add(ModpackModMatcher.NormalizeMatchKey(name));
                snapshot.SkinKeys.Add(ModpackModMatcher.NormalizeMatchKey(fileName));
                AddLocalFileName(snapshot, fileName);
            }

            // 内建实体过滤已与本体对齐：谓词与 LocalResourceCollection.ScanLoadedMods 同款
            // （modInfo 判空 + 排除 SurvivalCraftModEntity/FastDebugModEntity 内建实体）。
            IEnumerable<LocalResourceItem> loadedMods = ModsManager.ModListAll
                .Where(entity => entity?.modInfo != null)
                .Where(entity => entity is not SurvivalCraftModEntity && entity is not FastDebugModEntity)
                .Select(entity => new LocalResourceItem
                {
                    Type = ExternalContentType.Mod,
                    PackageName = entity.modInfo.PackageName,
                    LocalFilePath = entity.ModFilePath
                });
            foreach (LocalResourceItem mod in loadedMods.Concat(pendingMods ?? []))
            {
                string packageKey = ModpackModMatcher.NormalizeMatchKey(mod?.PackageName);
                string fileName = Storage.GetFileName(mod?.LocalFilePath ?? string.Empty);
                snapshot.ModPackageKeys.Add(packageKey);
                snapshot.ModFileKeys.Add(ModpackModMatcher.NormalizeMatchKey(mod?.LocalFilePath));
                AddLocalFileName(snapshot, fileName);
                if (!string.IsNullOrWhiteSpace(packageKey) && !string.IsNullOrWhiteSpace(fileName))
                {
                    snapshot.ModPackageFileNames[packageKey] = fileName;
                }
            }

            lock (m_sync)
            {
                m_cachedSnapshot = snapshot;
            }
            return snapshot;
        }

        public void Invalidate()
        {
            lock (m_sync)
            {
                m_cachedSnapshot = null;
            }
        }

        private static void AddLocalFileName(ModpackPresenceSnapshot snapshot, string fileName)
        {
            string safeFileName = Storage.GetFileName(fileName ?? string.Empty);
            string key = ModpackModMatcher.NormalizeMatchKey(safeFileName);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(safeFileName))
            {
                snapshot.LocalFileNames.Add(new KeyValuePair<string, string>(key, safeFileName));
            }
        }
    }
}
