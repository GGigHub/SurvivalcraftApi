using System;
using System.Collections.Generic;
using System.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// Builds local-resource collections and dependency availability snapshots
    /// without taking ownership of screen refreshes, dialogs, or scan tasks.
    /// </summary>
    internal static class LocalResourceCollection
    {

        // 以下扫描依赖引擎管理器的 Update*List()（对全局列表 Clear+Add 后枚举），
        // 主线程渲染可能并发枚举同一列表，必须主线程同步调用。
        public static List<LocalResourceItem> ScanFurniturePacks()
        {
            FurniturePacksManager.UpdateFurniturePacksList();
            return FurniturePacksManager.FurniturePackNames
                .Select(name => new LocalResourceItem
                {
                    Type = ExternalContentType.FurniturePack,
                    Name = name,
                    DisplayName = FurniturePacksManager.GetDisplayName(name),
                    CreationTime = FurniturePacksManager.GetCreationDate(name)
                })
                .OrderByDescending(item => item.CreationTime)
                .ToList();
        }

        public static List<LocalResourceItem> ScanBlocksTextures()
        {
            BlocksTexturesManager.UpdateBlocksTexturesList();
            // WorldInfos 每次访问可能构造新 ReadOnlyList：循环外取一次快照，避免 O(N×M) 重复构造。
            ReadOnlyList<WorldInfo> worlds = WorldsManager.WorldInfos;
            return BlocksTexturesManager.BlockTexturesNames
                .Select(name => new LocalResourceItem
                {
                    Type = ExternalContentType.BlocksTexture,
                    Name = name,
                    DisplayName = BlocksTexturesManager.GetDisplayName(name),
                    CreationTime = BlocksTexturesManager.GetCreationDate(name),
                    IsBuiltIn = BlocksTexturesManager.IsBuiltIn(name),
                    UseCount = worlds.Count(world => world.WorldSettings.BlocksTextureName == name)
                })
                .OrderBy(item => item.IsBuiltIn ? 0 : 1)
                .ThenBy(item => item.DisplayName, StringComparer.InvariantCulture)
                .ToList();
        }

        public static List<LocalResourceItem> ScanCharacterSkins()
        {
            CharacterSkinsManager.UpdateCharacterSkinsList();
            // WorldInfos 每次访问可能构造新 ReadOnlyList：循环外取一次快照，避免 O(N×M) 重复构造。
            ReadOnlyList<WorldInfo> worlds = WorldsManager.WorldInfos;
            return CharacterSkinsManager.CharacterSkinsNames
                .Select(name => new LocalResourceItem
                {
                    Type = ExternalContentType.CharacterSkin,
                    Name = name,
                    DisplayName = CharacterSkinsManager.GetDisplayName(name),
                    CreationTime = CharacterSkinsManager.GetCreationDate(name),
                    IsBuiltIn = CharacterSkinsManager.IsBuiltIn(name),
                    UseCount = worlds.Count(world =>
                        world.PlayerInfos.Any(player => player.CharacterSkinName == name))
                })
                .OrderBy(item => item.IsBuiltIn ? 0 : 1)
                .ThenBy(item => item.DisplayName, StringComparer.InvariantCulture)
                .ToList();
        }

        public static List<LocalResourceItem> ScanLoadedMods()
        {
            return ModsManager.ModListAll
                .Where(entity => entity?.modInfo != null)
                .Where(entity => entity is not SurvivalCraftModEntity && entity is not FastDebugModEntity)
                .Select(entity => new LocalResourceItem
                {
                    Type = ExternalContentType.Mod,
                    Name = entity.modInfo.PackageName,
                    DisplayName = entity.modInfo.Name,
                    // 与其余 CreationTime 来源同 Kind：引擎主路径为 Utc（兜底 2000-01-01 哨兵为
                    // Unspecified，tick 恒最小、ToLocalTime 原样，不影响排序/显示），显示端统一 ToLocalTime()。
                    CreationTime = DateTime.UtcNow,
                    LocalFilePath = entity.ModFilePath,
                    PackageName = entity.modInfo.PackageName,
                    Version = entity.modInfo.Version,
                    ModEntity = entity,
                    IsDisabled = entity.IsDisabled,
                    DisableReason = entity.DisableReason
                })
                .OrderBy(item => item.DisplayName, StringComparer.InvariantCulture)
                .ToList();
        }

        public static List<LocalResourceItem> BuildLocalMods(
            IEnumerable<LocalResourceItem> pendingMods,
            IReadOnlyDictionary<string, LocalModPendingActionEntry> pendingActions)
        {
            List<LocalResourceItem> loadedMods = ScanLoadedMods() ?? [];
            LocalResourceItem[] stagedMods = LocalModCollectionRules.OrderPendingMods(pendingMods);
            foreach (LocalResourceItem item in loadedMods.Concat(stagedMods))
            {
                ApplyPendingAction(item, pendingActions);
            }

            LocalModCollectionRules.MarkDuplicatePackageNames(loadedMods, stagedMods);
            loadedMods.InsertRange(0, stagedMods);
            return loadedMods;
        }

        // pendingMods 为 null 表示缓存尚未就绪，保留原有同步扫描回退；空集合则是已完成且无待重启模组的缓存。
        public static ModDependencySnapshot CaptureDependencySnapshot(
            IEnumerable<LocalResourceItem> pendingMods,
            Func<IEnumerable<ModEntity>> loadedModEntities,
            Func<PendingModPackageSets> pendingPackageSetsProvider)
        {
            PendingModPackageSets packageSets = pendingMods == null
                ? pendingPackageSetsProvider()
                : GetPendingPackageSets(pendingMods);
            return new ModDependencySnapshot(GetInstalledPackageKeys(loadedModEntities), packageSets);
        }

        public static void ApplyPendingAction(
            LocalResourceItem resourceItem,
            IReadOnlyDictionary<string, LocalModPendingActionEntry> pendingActions)
        {
            if (resourceItem?.Type != ExternalContentType.Mod)
            {
                return;
            }

            string key = PendingLocalModMetadataReader.NormalizeStoragePath(
                LocalResourceIdentity.GetModStoragePath(resourceItem));
            if (string.IsNullOrWhiteSpace(key)
                || pendingActions == null
                || !pendingActions.TryGetValue(key, out LocalModPendingActionEntry entry)
                || entry == null)
            {
                resourceItem.PendingAction = LocalModPendingActionType.None;
                return;
            }

            resourceItem.PendingAction = entry.ActionType;
        }

        private static HashSet<string> GetInstalledPackageKeys(Func<IEnumerable<ModEntity>> loadedModEntities)
        {
            HashSet<string> packages = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntity localEntity in loadedModEntities() ?? [])
            {
                if (localEntity?.modInfo == null || string.IsNullOrWhiteSpace(localEntity.modInfo.PackageName))
                {
                    continue;
                }

                string packageName = localEntity.modInfo.PackageName.Trim();
                packages.Add(packageName);
                if (!string.IsNullOrWhiteSpace(localEntity.modInfo.Version))
                {
                    packages.Add(ModDependencyAvailabilityRules.MakePackageVersionKey(
                        packageName,
                        localEntity.modInfo.Version));
                }
            }
            return packages;
        }

        private static PendingModPackageSets GetPendingPackageSets(IEnumerable<LocalResourceItem> pendingMods)
        {
            PendingModPackageSets result = new();
            foreach (LocalResourceItem item in pendingMods)
            {
                PendingLocalModInfo info = item?.Tag as PendingLocalModInfo;
                string packageName = !string.IsNullOrWhiteSpace(info?.PackageName)
                    ? info.PackageName.Trim()
                    : Storage.GetFileNameWithoutExtensionBCL(
                        Storage.GetFileName(item?.LocalFilePath ?? string.Empty));
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                HashSet<string> target = item.IsIncompatibleWithCurrentGame
                    ? result.IncompatiblePackages
                    : result.PendingRestartPackages;
                target.Add(packageName);
                if (!string.IsNullOrWhiteSpace(info?.Version))
                {
                    target.Add(ModDependencyAvailabilityRules.MakePackageVersionKey(packageName, info.Version));
                }
            }
            return result;
        }
    }

    internal sealed class ModDependencySnapshot
    {
        private readonly ISet<string> m_loadedPackages;
        private readonly ISet<string> m_pendingRestartPackages;
        private readonly ISet<string> m_incompatiblePackages;

        public ModDependencySnapshot(
            ISet<string> loadedPackages,
            PendingModPackageSets pendingPackageSets)
        {
            m_loadedPackages = loadedPackages ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            m_pendingRestartPackages = pendingPackageSets?.PendingRestartPackages
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            m_incompatiblePackages = pendingPackageSets?.IncompatiblePackages
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public ModDependencyAvailability Evaluate(ModDependencyInfo dependency)
        {
            return ModDependencyAvailabilityRules.Evaluate(
                dependency,
                m_loadedPackages,
                m_pendingRestartPackages,
                m_incompatiblePackages);
        }

        public IEnumerable<ModDependencyInfo> GetUnavailableDependencies(IEnumerable<ModDependencyInfo> dependencies)
        {
            foreach (ModDependencyInfo dependency in dependencies ?? [])
            {
                ModDependencyAvailability availability = Evaluate(dependency);
                if (availability is ModDependencyAvailability.Missing or ModDependencyAvailability.Incompatible)
                {
                    yield return dependency;
                }
            }
        }
    }
}
