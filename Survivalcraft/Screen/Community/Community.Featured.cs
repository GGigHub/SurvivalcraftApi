using System;
using System.Collections.Generic;
using System.Globalization;
using Engine;

namespace Game
{
    internal partial class CommunityScreen
    {
        private void LoadFeatured()
        {
            m_featuredResult = null;
            if (GetEffectiveSection() != CommunitySection.All)
            {
                RenderSelectedFeatured();
            }

            m_featuredSlot.Start(async (requestId, _) =>
            {
                try
                {
                    CommunityFeaturedData featured =
                        await CommunityManager.LoadFeaturedAsync(CommunityManager.ApiClient, CurrentApiBaseUrl);
                    return new FeaturedLoadResult
                    {
                        RequestId = requestId,
                        TopResources = featured.TopResources,
                        RecentUpdates = featured.RecentUpdates
                    };
                }
                catch (Exception ex)
                {
                    // 整体失败降级为空列表 + Error；单侧失败已由 ContentService 降级为部分内容。
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("CommunityScreen", "LoadFeaturedFailed"), ex.Message));
                    return new FeaturedLoadResult
                    {
                        RequestId = requestId,
                        Error = ex.Message,
                        TopResources = [],
                        RecentUpdates = []
                    };
                }
            });
        }

        private void ApplyFeaturedResult(FeaturedLoadResult loadResult)
        {
            m_featuredResult = loadResult;
            RenderSelectedFeatured();
        }

        private void RenderSelectedFeatured()
        {
            CommunitySection effectiveSection = GetEffectiveSection();
            if (effectiveSection == CommunitySection.All)
            {
                return;
            }

            m_postsPresenter.Clear(disposeIcons: true);
            m_postsPresenter.InvalidatePostDetailLoad();
            m_postsPresenter.TotalPages = 1;

            string sectionName = LanguageControl.GetEnumDisplayName(effectiveSection);
            if (m_featuredResult == null)
            {
                m_postsPresenter.StatusLabel.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("CommunityScreen", "Community", "Featured", "Loading"), sectionName);
                UpdatePaginationButtons();
                return;
            }

            if (!string.IsNullOrEmpty(m_featuredResult.Error))
            {
                m_postsPresenter.ShowLoadFailed(sectionName);
                m_postsPresenter.AddRoot(new TreeViewNode(LanguageControl.Get("CommunityScreen", "Community", "Featured", "FailedToLoadFeaturedResources"), "") { Selectable = false });
                UpdatePaginationButtons();
                return;
            }

            IReadOnlyList<PostItem> items = effectiveSection == CommunitySection.Trending
                ? m_featuredResult.TopResources
                : m_featuredResult.RecentUpdates;
            if (items == null || items.Count == 0)
            {
                m_postsPresenter.AddRoot(new TreeViewNode(LanguageControl.Get("CommunityScreen", "Community", "Featured", "NoData"), "") { Selectable = false });
            }
            else
            {
                foreach (PostItem post in items)
                {
                    // 服务端脏数据防线：null PostItem 会在 CreatePostNode 解引用 CreatedAt/FileType 崩帧。
                    if (post == null)
                    {
                        continue;
                    }
                    m_postsPresenter.AddRoot(m_postsPresenter.CreatePostNode(post));
                }
            }

            int count = items?.Count ?? 0;
            m_postsPresenter.StatusLabel.Text = $"{sectionName} ({count})";
            m_postsPresenter.MarkDirty();
            UpdatePaginationButtons();
        }
    }
}
