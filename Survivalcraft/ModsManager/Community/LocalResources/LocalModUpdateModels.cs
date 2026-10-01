using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace Game
{
    /// <summary>可发起网络请求的 DTO 公共契约：按 RequestId 防陈旧（与请求 DTO 同层）。</summary>
    internal interface IRequestIdRequest
    {
        int RequestId { get; set; }
    }

    internal sealed class LocalModUpdateCheckRequest : IRequestIdRequest
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public int CachedPostId { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public string FileName { get; set; }
        public string CurrentVersion { get; set; }
        public string PackageName { get; set; }
        public string RepositoryUrl { get; set; }
        public string CurrentApiVersion { get; set; }
        public string CurrentGameVersion { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public bool RequirePublishedPost { get; set; }
    }

    internal sealed class LocalModAutoUpdateScanRequest : IRequestIdRequest
    {
        public int RequestId { get; set; }
        public string Signature { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public string CurrentApiVersion { get; set; }
        public string CurrentGameVersion { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public IReadOnlyList<LocalModUpdateTarget> Targets { get; set; }
    }

    internal sealed class LocalModUpdateTarget
    {
        public string ResourceKey { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string CurrentVersion { get; set; }
        public string PackageName { get; set; }
        public string RepositoryUrl { get; set; }
        public int CachedPostId { get; set; }
    }

    internal sealed class LocalModAutoUpdateScanResult
    {
        public int RequestId { get; set; }
        public string Signature { get; set; }
        public List<LocalModAutoUpdateScanEntry> Entries { get; set; }
        public string Error { get; set; }
    }

    internal sealed class LocalModAutoUpdateScanEntry
    {
        public string ResourceKey { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public LocalModCommunityStatusInfo Status { get; set; }
        public LocalModUpdateCheckResult UpdateInfo { get; set; }
    }

    internal sealed class LocalModCompatibilityContext
    {
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
    }

    internal sealed class LocalModCommunityUpdateRequest
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public int CachedPostId { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public string FileName { get; set; }
        public string CurrentVersion { get; set; }
        public string PackageName { get; set; }
        public Func<PostVersionItem, bool> IsCompatibleVersion { get; set; }
        public Func<Task<Func<PostVersionItem, bool>>> LoadCompatibilityPredicateAsync { get; set; }
    }

    internal sealed class LocalModRepositoryUpdateRequest
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public string FileName { get; set; }
        public string CurrentVersion { get; set; }
        public string PackageName { get; set; }
        public string RepositoryUrl { get; set; }
    }

    internal sealed class LocalModUpdateCheckResult
    {
        private string m_currentVersion;
        private string m_latestVersion;

        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public string SourceFileName { get; set; }
        public int PostId { get; set; }
        public string PostTitle { get; set; }
        public string CurrentVersion
        {
            get => m_currentVersion;
            set
            {
                m_currentVersion = value;
                CurrentNuGetVersion = ExternalVersionParser.ParseOrNull(value);
            }
        }
        public NuGetVersion CurrentNuGetVersion { get; private set; }
        public string LatestVersion
        {
            get => m_latestVersion;
            set
            {
                m_latestVersion = value;
                LatestNuGetVersion = ExternalVersionParser.ParseOrNull(value);
            }
        }
        public NuGetVersion LatestNuGetVersion { get; private set; }
        public PostVersionItem LatestVersionItem { get; set; }
        public PostFileItem File { get; set; }
        // 仓库 release 来源的更新说明原文（纯文本）；社区帖子来源为 null（内容在 LatestVersionItem.Content）。
        public string ReleaseNotes { get; set; }
        public bool HasUpdate { get; set; }
        public string NoUpdateReason { get; set; }
        public string Error { get; set; }
    }
}
