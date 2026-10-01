using Engine;
using NuGet.Versioning;

namespace Game {
    /// <summary>社区帖子相对本机的安装状态；UI 文案由表现层按状态映射。</summary>
    enum LocalModInstallState {
        None,
        Installed,
        UpdateAvailable
    }

    /// <summary>
    /// 社区帖子/版本相对本机模组的安装与更新状态判定：本地快照捕获、重复导入前的本地
    /// 已装文件探测、帖子 ID 关联（评价缓存多键 + 社区发布状态缓存）与版本比较。
    /// 返回状态枚举，不产出 UI 文案。
    /// </summary>
    static class LocalModInstallStatus {
        static readonly Dictionary<string, LocalModCommunityStatusInfo> s_communityStatusCache = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, LocalModUpdateCheckResult> s_updateDetailsCache = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, LocalModCommunityStatusInfo> LocalModCommunityStatusCache => s_communityStatusCache;

        public static Dictionary<string, LocalModUpdateCheckResult> LocalModUpdateDetailsCache => s_updateDetailsCache;

        /// <summary>
        /// Finds existing game-managed resources before imports request replacement.
        /// 纯文件系统查询（不触引擎管理器全局列表）：家具包/贴图/皮肤的内部名即磁盘文件名，
        /// 各自目录直拼判定；模组可放 ModsPath 子目录（少见），递归匹配。
        /// </summary>
        public static string FindExactLocalDownloadFilePath(ExternalContentType type, string fileName) {
            if (string.IsNullOrWhiteSpace(fileName)) {
                return null;
            }
            return type switch {
                ExternalContentType.Mod or ExternalContentType.NetMod => FindFileRecursive(ModsManager.ModsPath, fileName),
                ExternalContentType.FurniturePack => FindInDirectory(FurniturePacksManager.FurniturePacksDirectoryName, fileName),
                ExternalContentType.BlocksTexture => FindInDirectory(BlocksTexturesManager.BlockTexturesDirectoryName, fileName),
                ExternalContentType.CharacterSkin => FindInDirectory(CharacterSkinsManager.CharacterSkinsDirectoryName, fileName),
                _ => null
            };
        }

        static string FindInDirectory(string directoryName, string fileName) {
            if (string.IsNullOrWhiteSpace(directoryName)) {
                return null;
            }
            string candidate = Storage.CombinePaths(directoryName, fileName);
            return Storage.FileExists(candidate) ? candidate : null;
        }

        static string FindFileRecursive(string directory, string fileName) {
            string direct = FindInDirectory(directory, fileName);
            if (direct != null) {
                return direct;
            }
            foreach (string subDirectory in Storage.ListDirectoryNames(directory)) {
                string found = FindFileRecursive(Storage.CombinePaths(directory, subDirectory), fileName);
                if (found != null) {
                    return found;
                }
            }
            return null;
        }

        public static List<LocalResourceItem> GetCommunityLocalModSnapshot() {
            // 已加载与待重启实体同在 ModListAll，投影直取；按规范化路径去重
            //（覆盖升级同路径“已加载 + 待重启”双实体只计已加载那条，状态判定只计一次）。
            // 内置实体沿用 ScanLoadedMods 的排除口径。
            List<LocalResourceItem> result = [];
            HashSet<string> knownPaths = new(StringComparer.OrdinalIgnoreCase);
            // ModListAll 主线程排序时 Clear+AddRange 不可并发枚举，跨线程读必须封送
            Dispatcher.Dispatch(
                delegate {
                    foreach (ModEntity entity in ModsManager.ModListAll) {
                        if (entity?.modInfo == null
                            || entity is SurvivalCraftModEntity
                            || entity is FastDebugModEntity) {
                            continue;
                        }
                        string path = entity.ModFilePath == null ? string.Empty : ModsManager.NormalizeStoragePath(entity.ModFilePath);
                        if (entity.IsPendingRestart) {
                            if (!string.IsNullOrWhiteSpace(path)
                                && !knownPaths.Add(path)) {
                                continue; // 覆盖升级：同路径已加载实体已计入，状态判定只计一次
                            }
                            result.Add(LocalResourceCollection.ToLocalResourceItem(entity));
                        }
                        else if (!entity.IsDisabled) {
                            knownPaths.Add(path);
                            result.Add(LocalResourceCollection.ToLocalResourceItem(entity));
                        }
                    }
                },
                true
            );
            return result;
        }

        public static bool IsInstalledCommunityModVersion(PostVersionItem version) =>
            IsInstalledCommunityModVersion(version, GetCommunityLocalModSnapshot());

        /// <summary>带外部快照的重载：调用方在批量判定（如整树刷行组件）时复用同一份快照，避免每节点重建。</summary>
        public static bool IsInstalledCommunityModVersion(PostVersionItem version, List<LocalResourceItem> communityLocalMods) {
            if (version?.ShowLocalModInstallStatus != true
                || version.PostId <= 0) {
                return false;
            }
            foreach (LocalResourceItem localMod in communityLocalMods) {
                string packageName = localMod.PackageName ?? localMod.ModEntity?.modInfo?.PackageName;
                string currentVersion = localMod.ModEntity?.modInfo?.Version ?? localMod.Version;
                if (string.IsNullOrWhiteSpace(currentVersion)) {
                    continue;
                }
                NuGetVersion currentNuGetVersion = localMod.ModEntity?.modInfo?.NuGetVersion;
                if (currentNuGetVersion == null) {
                    // 本地版本解析失败时无从比较，直接看下一个本地模组。
                    if (!ExternalVersionParser.TryParse(currentVersion, out currentNuGetVersion)) {
                        continue;
                    }
                }
                string localFileName = Storage.GetFileName(LocalResourceIdentity.GetModStoragePath(localMod) ?? localMod.Name);
                string resourceKey = LocalResourceIdentity.MakeModReviewKey(localMod);
                bool matchesPost = LocalModReviewState.GetCachedLocalModPostId(localMod) == version.PostId;
                if (!string.IsNullOrWhiteSpace(resourceKey)
                    && s_communityStatusCache.TryGetValue(resourceKey, out LocalModCommunityStatusInfo cachedStatus)
                    && cachedStatus?.IsPublished == true
                    && cachedStatus.PostId == version.PostId) {
                    matchesPost = true;
                }
                PostFileItem[] matchingFiles = CommunityLocalModMatchingRules.GetMatchingFiles(version, packageName, localFileName);
                bool hasPackageMetadata = version.Files?.Any(file => file != null && !string.IsNullOrWhiteSpace(file.PackageName)) == true;
                if (matchingFiles.Length == 0
                    && (!matchesPost || hasPackageMetadata)) {
                    continue;
                }

                // NuGetVersion 的 == 即 VersionRelease 语义，null 比较返回 false。
                if (version.NuGetVersion == currentNuGetVersion
                    || matchingFiles.Any(file => file.NuGetVersion == currentNuGetVersion)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>带外部快照的重载：调用方在批量判定（如整树刷行组件）时复用同一份快照，避免每节点重建。</summary>
        public static LocalModInstallState GetCommunityModInstallState(PostItem post, List<LocalResourceItem> communityLocalMods) {
            if (post?.ShowLocalModInstallStatus != true
                || post.Id <= 0) {
                return LocalModInstallState.None;
            }
            bool isInstalled = false;
            foreach (LocalResourceItem localMod in communityLocalMods) {
                string packageName = localMod.PackageName ?? localMod.ModEntity?.modInfo?.PackageName;
                string currentVersion = localMod.ModEntity?.modInfo?.Version ?? localMod.Version;
                NuGetVersion currentNuGetVersion = localMod.ModEntity?.modInfo?.NuGetVersion;
                if (currentNuGetVersion == null) {
                    ExternalVersionParser.TryParse(currentVersion, out currentNuGetVersion);
                }
                string localFileName = Storage.GetFileName(LocalResourceIdentity.GetModStoragePath(localMod) ?? localMod.Name);
                string resourceKey = LocalResourceIdentity.MakeModReviewKey(localMod);
                bool matchesPost = LocalModReviewState.GetCachedLocalModPostId(localMod) == post.Id
                    || CommunityLocalModMatchingRules.PostContainsLocalMod(post, packageName, localFileName);
                LocalModCommunityStatusInfo cachedStatus = null;
                if (!string.IsNullOrWhiteSpace(resourceKey)
                    && s_communityStatusCache.TryGetValue(resourceKey, out LocalModCommunityStatusInfo candidateStatus)
                    && candidateStatus?.IsPublished == true
                    && candidateStatus.PostId == post.Id) {
                    cachedStatus = candidateStatus;
                    matchesPost = true;
                }
                if (!matchesPost) {
                    continue;
                }
                isInstalled = true;
                if (cachedStatus?.HasUpdate == true) {
                    return LocalModInstallState.UpdateAvailable;
                }
                PostVersionItem latestVersion = ModUpdateSelector.FindLatestVersion(
                    post.PostVersions,
                    packageName,
                    localFileName,
                    version => ModCompatibility.IsCompatibleWithCurrentVersion(
                        version,
                        VersionCatalogCache.CurrentApiVersion,
                        VersionCatalogCache.CurrentGameVersion,
                        VersionCatalogCache.CachedGameVersionsById,
                        VersionCatalogCache.CachedVersionCatalog
                    )
                );
                if (latestVersion != null
                    && ModUpdateSelector.IsNewer(latestVersion.NuGetVersion, currentNuGetVersion)) {
                    return LocalModInstallState.UpdateAvailable;
                }
            }
            return isInstalled ? LocalModInstallState.Installed : LocalModInstallState.None;
        }
    }
}