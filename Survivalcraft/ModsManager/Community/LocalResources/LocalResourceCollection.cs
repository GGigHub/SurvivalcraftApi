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
                .Select(ToLocalResourceItem)
                .OrderBy(item => item.DisplayName, StringComparer.InvariantCulture)
                .ToList();
        }

        /// <summary>
        ///     ModEntity → LocalResourceItem 投影。IsIncompatibleWithCurrentGame 展示时实时判定
        ///     （NuGet ApiVersionRange + 旧前缀特判，叠加下载前兼容覆盖），不落实体字段。
        /// </summary>
        public static LocalResourceItem ToLocalResourceItem(ModEntity entity)
        {
            // 待重启实体的 icon 只解了数据未上传 GPU，投影供 UI 展示前在主线程补传
            entity?.EnsureIconTexture();
            return new LocalResourceItem
            {
                Type = ExternalContentType.Mod,
                Name = entity?.modInfo?.PackageName,
                DisplayName = string.IsNullOrWhiteSpace(entity?.modInfo?.Name)
                    ? Storage.GetFileNameWithoutExtensionBCL(Storage.GetFileName(entity?.ModFilePath ?? string.Empty))
                    : entity.modInfo.Name,
                CreationTime = entity?.LastWriteTimeUtc ?? DateTime.UtcNow,
                LocalFilePath = entity?.ModFilePath,
                PackageName = entity?.modInfo?.PackageName,
                Version = entity?.modInfo?.Version,
                ModEntity = entity,
                IsDisabled = entity?.IsDisabled == true,
                DisableReason = entity?.DisableReason ?? ModDisableReason.Unknown,
                RequiresRestartToLoad = entity?.IsPendingRestart == true,
                IsIncompatibleWithCurrentGame = IsModIncompatibleWithCurrentGame(entity)
                    // 下载前已判定兼容的版本沿用该结论（兼容覆盖缓存的判定点收敛在投影处），避免与下载提示前后不一致
                    && !LocalModStateStore.HasPendingLocalModCompatibilityOverride(
                        entity?.modInfo?.PackageName,
                        entity?.ModFilePath,
                        entity?.modInfo?.Version),
                Tag = entity
            };
        }

        // 兼容判定独立谓词：与展示文案解耦。无 ApiVersion 声明不判不兼容（展示层另有“未知”文案）。
        private static bool IsModIncompatibleWithCurrentGame(ModEntity entity)
        {
            if (entity?.modInfo == null || string.IsNullOrEmpty(entity.modInfo.ApiVersion))
            {
                return false;
            }
            if (entity.modInfo.ApiVersion.StartsWith("1.4")
                || entity.modInfo.ApiVersion.StartsWith("1.5")
                || entity.modInfo.ApiVersion.StartsWith("1.6")
                || entity.modInfo.ApiVersion.StartsWith("1.7"))
            {
                return true;
            }
            return entity.modInfo.ApiVersionRange != null
                && !entity.modInfo.ApiVersionRange.Satisfies(ModsManager.APINuGetVersion);
        }

        public static List<LocalResourceItem> GetPendingRestartLocalMods()
        {
            List<LocalResourceItem> result = [];
            // ModListAll 主线程排序时 Clear+AddRange 不可并发枚举，跨线程读必须封送
            Dispatcher.Dispatch(delegate
            {
                foreach (ModEntity entity in ModsManager.ModListAll)
                {
                    if (entity?.IsPendingRestart == true)
                    {
                        result.Add(ToLocalResourceItem(entity));
                    }
                }
            }, waitUntilCompleted: true);
            return result;
        }

        /// <summary>
        ///     待重启包集合（包名 + 包名@版本键），替代原扫描器的 GetPackageSets，同键规则；
        ///     供依赖可用性快照在缓存未就绪时实时取数。
        /// </summary>
        public static PendingModPackageSets GetPendingPackageSetsFromEntities()
        {
            PendingModPackageSets result = new();
            Dispatcher.Dispatch(delegate
            {
                foreach (ModEntity entity in ModsManager.ModListAll)
                {
                    if (entity?.IsPendingRestart != true || entity.modInfo == null)
                    {
                        continue;
                    }
                    HashSet<string> target = IsModIncompatibleWithCurrentGame(entity)
                        ? result.IncompatiblePackages
                        : result.PendingRestartPackages;
                    target.Add(entity.modInfo.PackageName);
                    if (!string.IsNullOrWhiteSpace(entity.modInfo.Version))
                    {
                        target.Add(ModDependencyAvailabilityRules.MakePackageVersionKey(
                            entity.modInfo.PackageName,
                            entity.modInfo.Version));
                    }
                }
            }, waitUntilCompleted: true);
            return result;
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
                ? GetPendingPackageSetsFromEntities()
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

            string key = ModsManager.NormalizeStoragePath(
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
                string packageName = !string.IsNullOrWhiteSpace(item?.PackageName)
                    ? item.PackageName.Trim()
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
                if (!string.IsNullOrWhiteSpace(item.Version))
                {
                    target.Add(ModDependencyAvailabilityRules.MakePackageVersionKey(packageName, item.Version));
                }
            }
            return result;
        }
    }

    // 待重启包集合：依赖可用性快照的“待重启/不兼容”两分组（原随旧磁盘扫描器定义，扫描器删除后迁此）
    internal sealed class PendingModPackageSets
    {
        public HashSet<string> PendingRestartPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncompatiblePackages { get; } = new(StringComparer.OrdinalIgnoreCase);
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
