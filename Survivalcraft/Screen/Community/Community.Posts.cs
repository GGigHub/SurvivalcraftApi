using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Engine;

namespace Game
{
    internal partial class CommunityScreen
    {
        private void LoadCommunityPosts()
        {
            m_postsPresenter.InvalidatePostDetailLoad();
            m_postsPresenter.Clear(disposeIcons: true);
            CommunityFileType fileType = m_selectedFileType;
            string token = AuthSessionManager.Instance.CurrentToken;
            string searchTitle = m_searchTitle?.Trim() ?? string.Empty;
            bool useSearchIgnore = !string.IsNullOrEmpty(searchTitle);
            CommunityFileType resultFileType = useSearchIgnore && m_ignoreFileTypeFilter ? CommunityFileType.Unknown : fileType;
            string apiBaseUrl = CurrentApiBaseUrl;
            m_postsPresenter.TotalPages = MathUtils.Max(m_postsPresenter.Page, 1);
            m_postsPresenter.StatusLabel.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "LoadingFormat"), string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "CommunityFormat"), EnumDisplayName.GetEnumDisplayName(resultFileType)));
            bool needVersionFilter = (!useSearchIgnore || !m_ignoreVersionFilter) && m_onlyCurrentVersion;
            bool needVersionCatalog = VersionCatalogCache.ShouldLoadVersionCatalogForFileType(resultFileType);
            VersionCatalogCache.TryGetCachedVersionCatalog(apiBaseUrl, out VersionListItem[] cachedCatalog, out Dictionary<int, string> cachedVersionMap, out int? cachedPluginVersionId);
            string currentApiVersion = VersionCatalogCache.CurrentApiVersion;

            m_postsLoadSlot.Start(async (requestId, _) =>
            {
                CommunityPostsLoad load = await CommunityManager.LoadPostsPageAsync(
                    CommunityManager.ApiClient,
                    new CommunityPostsLoadRequest
                    {
                        ApiBaseUrl = apiBaseUrl,
                        Token = token,
                        Page = m_postsPresenter.Page,
                        FileType = fileType,
                        OrderType = m_orderType,
                        SearchTitle = searchTitle,
                        IgnoreOrder = m_ignoreOrderFilter,
                        IgnoreFileType = m_ignoreFileTypeFilter,
                        LoadVersionCatalog = needVersionCatalog,
                        FilterToCurrentApiVersion = needVersionFilter,
                        CurrentApiVersion = currentApiVersion,
                        CachedCatalog = cachedCatalog,
                        CachedVersionMap = cachedVersionMap,
                        CachedPluginVersionId = cachedPluginVersionId,
                        ResolvePluginVersionIdAsync = () => VersionCatalogCache.ResolvePluginVersionIdAsync(apiBaseUrl, token)
                    });
                if (!string.IsNullOrEmpty(load.Error))
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "LoadCommunityPostsFailed"), load.Error));
                }
                return new PostsLoadResult
                {
                    RequestId = requestId,
                    FileType = resultFileType,
                    Source = ResourceSource.Community,
                    ApiBaseUrl = apiBaseUrl,
                    Response = load.Response,
                    VersionCatalog = load.VersionCatalog,
                    GameVersionsById = load.GameVersionsById,
                    Error = load.Error
                };
            });
        }

        private void RefreshPanel()
        {
            UpdateOrderButtonState();
            m_postsPresenter.MarkDirty();
            m_postsLoadSlot.Invalidate();
            m_postsPresenter.InvalidatePostDetailLoad();
            m_postsPresenter.TotalPages = 1;
            m_postsPresenter.Clear(disposeIcons: true);
            RefreshCurrentPage();
        }

        // 按钮 Text/IsEnabled 完全由四个布尔输入决定；签名不变时跳过赋值，
        // 避免每帧 4 次 LanguageControl.Get（params 数组 + JsonNode 查找的堆分配）。
        private int m_downloadButtonsState = -1;

        private void UpdateDownloadButtons()
        {
            // SelectedNode 遮蔽属性含 O(n) 可达性检查，每帧多次调用时存局部变量只走一次。
            TreeViewNode selectedNode = m_postsTree.SelectedNode;
            PostFileItem file = selectedNode?.Tag as PostFileItem;
            PostItem post = selectedNode?.Tag as PostItem;
            PostVersionItem version = selectedNode?.Tag as PostVersionItem;
            PostFileItem downloadTarget = file ?? PostTreePresenter.GetSingleVersionFile(version);
            bool downloadBusy = DownloadFlowManager.Instance.IsBusy;
            bool dialogLoadRunning = m_postDialogSlot.IsRunning;
            bool hasDownloadTarget = downloadTarget != null;
            bool hasDetailsTarget = post != null || version != null;
            int state = (hasDownloadTarget ? 1 : 0)
                | (downloadBusy ? 2 : 0)
                | (hasDetailsTarget ? 4 : 0)
                | (dialogLoadRunning ? 8 : 0);
            if (state != m_downloadButtonsState)
            {
                m_downloadButtonsState = state;
                m_downloadButton.IsEnabled = hasDownloadTarget && !downloadBusy;
                m_downloadButton.Text = downloadBusy
                    ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "Downloading")
                    : LanguageControl.Get("ScbbsPlusCommon", "Text", "Download");
                m_detailsButton.IsEnabled = hasDetailsTarget && !dialogLoadRunning;
                m_detailsButton.Text = dialogLoadRunning ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "LoadingProgress") : LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "Details");
            }

            if (m_downloadButton.IsClicked && downloadTarget != null)
            {
                DownloadFlowManager.Instance.StartDownload(downloadTarget, version);
            }
            if (m_detailsButton.IsClicked && post != null && !m_postDialogSlot.IsRunning)
            {
                StartPostDetailsDialogLoad(post);
            }
            if (m_detailsButton.IsClicked && post == null && version != null && !m_postDialogSlot.IsRunning)
            {
                if (HasVersionDetailContent(version) || version.PostId <= 0)
                {
                    ShowPostVersionDetailsDialog(version);
                }
                else
                {
                    StartPostVersionDetailsDialogLoad(version);
                }
            }
        }

        private void ShowPostVersionDetailsDialog(PostVersionItem version)
        {
            string title = string.IsNullOrWhiteSpace(version?.Title)
                ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "VersionDetails")
                : version.Title;
            DialogsManager.ShowDialog(null, new CommunityPostDetailsDialog(title, CommunityVersionDetailFormatter.GetVersionDetailHtml(version), CurrentApiBaseUrl, enableAutoTranslate: true));
        }

        private void StartPostVersionDetailsDialogLoad(PostVersionItem version)
        {
            if (version == null)
            {
                return;
            }

            string apiBaseUrl = CurrentApiBaseUrl;
            m_postDialogSlot.TryStart(async (requestId, _) =>
            {
                string token = AuthSessionManager.Instance.CurrentToken;
                CommunityPostDetailLoad load = await CommunityManager.LoadPostDetailAsync(
                    CommunityManager.ApiClient,
                    apiBaseUrl,
                    token,
                    version.PostId);
                return new PostDetailsDialogLoadResult
                {
                    RequestId = requestId,
                    SourceVersion = version,
                    ResolvedVersion = FindMatchingPostVersion(load.Response?.Data, version) ?? version,
                    ShowVersionDetails = true,
                    Response = load.Response,
                    Error = load.Error
                };
            }, out _);
        }

        private void StartPostDetailsDialogLoad(PostItem post, bool includeStats = false)
        {
            string apiBaseUrl = CurrentApiBaseUrl;
            m_postDialogSlot.TryStart(async (requestId, _) =>
            {
                string token = AuthSessionManager.Instance.CurrentToken;
                CommunityPostDetailLoad load = await CommunityManager.LoadPostDetailAsync(
                    CommunityManager.ApiClient,
                    apiBaseUrl,
                    token,
                    post.Id,
                    includeStats);
                return new PostDetailsDialogLoadResult
                {
                    RequestId = requestId,
                    SourcePost = post,
                    Response = load.Response,
                    IncludeStats = includeStats,
                    Stats = load.Stats,
                    StatsError = load.StatsError,
                    Error = load.Error
                };
            }, out _);
        }

        private void UpdatePostDetailsDialogTask()
        {
            if (m_postDialogSlot.TryTake(
                    out PostDetailsDialogLoadResult result,
                    error => new PostDetailsDialogLoadResult
                    {
                        RequestId = m_postDialogSlot.RequestId,
                        Error = error.Message
                    }))
            {
                ShowPostDetailsDialog(result);
            }
        }

        private void ShowPostDetailsDialog(PostDetailsDialogLoadResult result)
        {
            if (result?.ShowVersionDetails == true)
            {
                if (!string.IsNullOrEmpty(result.Error))
                {
                    DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "FailedToLoadPostDetails"), result.Error), LanguageControl.Ok, null, null));
                    return;
                }

                ShowPostVersionDetailsDialog(result.ResolvedVersion ?? result.SourceVersion);
                return;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "FailedToLoadPostDetails"), result.Error), LanguageControl.Ok, null, null));
                return;
            }

            PostItem detail = result.Response?.Data ?? result.SourcePost;
            DialogsManager.ShowDialog(null, new CommunityPostDetailsDialog(
                detail?.Title ?? result.SourcePost?.Title,
                detail?.Content,
                CurrentApiBaseUrl,
                enableAutoTranslate: true,
                buildHeader: result.IncludeStats
                    ? panel => PostDetailsHeaderBuilder.AppendPublishStats(panel, result.Stats, result.StatsError)
                    : null));
        }

        private static bool HasVersionDetailContent(PostVersionItem version)
        {
            if (!string.IsNullOrWhiteSpace(version?.Content))
            {
                return true;
            }

            if (version?.ExtensionData == null)
            {
                return false;
            }

            string[] keys = ["content", "detail", "description", "remark", "body"];
            foreach (string key in keys)
            {
                if (version.ExtensionData.TryGetValue(key, out JsonElement element)
                    && element.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(element.GetString()))
                {
                    return true;
                }
            }

            return false;
        }

        internal static PostVersionItem FindMatchingPostVersion(PostItem detail, PostVersionItem sourceVersion)
        {
            if (detail?.PostVersions == null || sourceVersion == null)
            {
                return null;
            }

            if (sourceVersion.Id > 0)
            {
                foreach (PostVersionItem candidate in detail.PostVersions)
                {
                    if (candidate?.Id == sourceVersion.Id)
                    {
                        return candidate;
                    }
                }
            }

            foreach (PostVersionItem candidate in detail.PostVersions)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.Title, sourceVersion.Title, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Version, sourceVersion.Version, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.CreatedAt, sourceVersion.CreatedAt, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return detail.PostVersions.Length == 1 ? detail.PostVersions[0] : null;
        }
    }
}
