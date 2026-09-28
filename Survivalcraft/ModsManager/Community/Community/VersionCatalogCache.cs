using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>
    /// 版本目录与 plugin 版本匹配的进程级缓存：三屏（本地/社区/我的发布）共用一份目录、
    /// 游戏版本映射与 plugin 版本匹配结果。缓存对象不可变快照替换，后台任务整引用读取。
    /// </summary>
    internal static class VersionCatalogCache
    {
        /// <summary>
        /// 目录快照：BaseUrl/目录/映射打包成单个不可变对象整引用替换，
        /// 读取端要么读到完整组合要么读到旧组合，不会出现新 BaseUrl 配旧目录的撕裂。
        /// </summary>
        private sealed class CatalogSnapshot
        {
            public CatalogSnapshot(string baseUrl, VersionListItem[] catalog, Dictionary<int, string> gameVersionsById)
            {
                BaseUrl = baseUrl;
                Catalog = catalog;
                GameVersionsById = gameVersionsById;
            }

            public string BaseUrl { get; }
            public VersionListItem[] Catalog { get; }
            public Dictionary<int, string> GameVersionsById { get; }
        }

        private static CatalogSnapshot s_catalog;
        private static PluginVersionCache s_pluginVersion;

        /// <summary>
        /// plugin 版本匹配缓存的不可变快照：后台任务整对象替换，UI 线程整引用读取，杜绝字段组合撕裂。
        /// Resolved 区分"已判定且无匹配"（负缓存）与"尚未判定"，负缓存命中时直接短路 HTTP 往返。
        /// </summary>
        internal sealed class PluginVersionCache
        {
            public bool Resolved;
            public int? Id;
            public string ApiVersion;
            public string GameVersion;
            public string BaseUrl;
        }

        public static VersionListItem[] CachedVersionCatalog => s_catalog?.Catalog;

        public static Dictionary<int, string> CachedGameVersionsById => s_catalog?.GameVersionsById;

        /// <summary>
        /// 写入目录快照并绑定来源服务器。唯一写入入口；
        /// TryGetCachedVersionCatalog 据此拒绝复用其它服务器的目录。
        /// </summary>
        public static void UpdateCatalog(
            string apiBaseUrl,
            VersionListItem[] versionCatalog,
            Dictionary<int, string> gameVersionsById)
        {
            s_catalog = new CatalogSnapshot(
                (apiBaseUrl ?? string.Empty).TrimEnd('/'),
                versionCatalog,
                gameVersionsById);
        }

        public static string CurrentGameVersion =>
            string.IsNullOrWhiteSpace(ModsManager.GameVersion)
                ? VersionsManager.Version
                : ModsManager.GameVersion;

        public static string CurrentApiVersion => ModsManager.APIVersionString;

        public static async Task<int?> ResolvePluginVersionIdAsync(string apiBaseUrl, string token)
        {
            string gameVersion = CurrentGameVersion;
            string apiVersion = CurrentApiVersion;
            if (string.IsNullOrWhiteSpace(apiVersion))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusVersionCatalogCache", "NoApiVersionSkippingPluginMatch"), gameVersion ?? "null"));
                return null;
            }

            // 缓存合为单不可变对象：后台任务与 UI 线程并发读写时引用赋值原子，不会读到撕裂的字段组合。
            PluginVersionCache cache = s_pluginVersion;
            if (cache?.Resolved == true
                && string.Equals(cache.ApiVersion, apiVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cache.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cache.BaseUrl, apiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return cache.Id;
            }

            VersionListItem matchedVersion = await CommunityManager.ResolvePluginVersionAsync(
                CommunityManager.ApiClient,
                apiBaseUrl,
                token,
                apiVersion);
            if (matchedVersion == null)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusVersionCatalogCache", "NoMatchingPluginVersion"), gameVersion ?? "null", apiVersion));
                s_pluginVersion = new PluginVersionCache
                {
                    Resolved = true,
                    Id = null,
                    ApiVersion = apiVersion,
                    GameVersion = gameVersion,
                    BaseUrl = apiBaseUrl
                };
                return null;
            }

            s_pluginVersion = new PluginVersionCache
            {
                Resolved = true,
                Id = matchedVersion.Id,
                ApiVersion = apiVersion,
                GameVersion = gameVersion,
                BaseUrl = apiBaseUrl
            };
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusVersionCatalogCache", "VersionMatchSucceeded"), gameVersion ?? "null", apiVersion, matchedVersion.Id, matchedVersion.Version, matchedVersion.Name));
            return matchedVersion.Id;
        }

        /// <summary>
        /// 该文件类型的帖子是否需要拉版本目录（模组类帖子要显示与当前版本的兼容性）。
        /// Unknown 也返回 true：列表级类型缺失时逐帖仍可能是 Mod，不能简化为 IsModLike。
        /// 社区屏与我的发布屏的列表加载共用这一判定。
        /// </summary>
        public static bool ShouldLoadVersionCatalogForFileType(ExternalContentType fileType)
        {
            return fileType is ExternalContentType.Unknown or ExternalContentType.Mod or ExternalContentType.NetMod;
        }

        public static bool TryGetCachedVersionCatalog(
            string apiBaseUrl,
            out VersionListItem[] versionCatalog,
            out Dictionary<int, string> gameVersionsById,
            out int? pluginVersionId)
        {
            versionCatalog = null;
            gameVersionsById = null;
            pluginVersionId = null;

            string apiVersion = CurrentApiVersion;
            string gameVersion = CurrentGameVersion;
            PluginVersionCache cache = s_pluginVersion;
            bool pluginCacheValid = cache?.Resolved == true
                && string.Equals(cache.ApiVersion, apiVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cache.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cache.BaseUrl, apiBaseUrl, StringComparison.OrdinalIgnoreCase);
            if (pluginCacheValid)
            {
                pluginVersionId = cache.Id;
            }

            CatalogSnapshot snapshot = s_catalog;
            if (snapshot?.Catalog == null || snapshot.Catalog.Length == 0)
            {
                return pluginCacheValid;
            }

            // 缺陷 6：目录与写入时的服务器绑定——切服务器后旧目录不得复用
            //（否则拿 A 的版本 id 向 B 发 gameVersionIds，列表空/结果错）。
            // 不匹配时目录部分视为未命中（plugin 缓存自身已带 BaseUrl 校验）。
            bool catalogValid = string.Equals(snapshot.BaseUrl, (apiBaseUrl ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            if (!catalogValid)
            {
                return pluginCacheValid;
            }

            versionCatalog = snapshot.Catalog;
            gameVersionsById = snapshot.GameVersionsById != null && snapshot.GameVersionsById.Count > 0
                ? snapshot.GameVersionsById
                : BuildGameVersionNameMap(snapshot.Catalog);
            return true;
        }

        /// <summary>版本目录 id → 显示名映射（跳过无效项并 Trim 名称）。</summary>
        internal static Dictionary<int, string> BuildGameVersionNameMap(VersionListItem[] versions)
        {
            Dictionary<int, string> result = new();
            foreach (VersionListItem version in versions ?? [])
            {
                if (version == null || version.Id <= 0 || string.IsNullOrWhiteSpace(version.Name))
                {
                    continue;
                }

                result[version.Id] = version.Name.Trim();
            }
            return result;
        }
    }
}
