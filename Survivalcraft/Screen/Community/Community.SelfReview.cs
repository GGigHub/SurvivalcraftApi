using System;
using System.Linq;
using Engine;

namespace Game
{
    internal partial class CommunityScreen
    {
        private const string SelfReviewFileName = "ScbbsPlus.scmod";
        private const double SelfReviewPromptRequiredSeconds = 30.0 * 60.0;

        private void TryShowSelfReviewPromptAfterEnoughUse()
        {
            if (m_firstDownloadSelfReviewPromptShown || m_selfReviewPromptActive)
            {
                return;
            }

            if (GetSelfReviewPlaySeconds() < SelfReviewPromptRequiredSeconds)
            {
                return;
            }

            m_firstDownloadSelfReviewPromptShown = true;
            m_selfReviewPromptActive = true;
            ShowToastPrompt(
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "SelfReview", "LikeTheCommunityExpansionMod"),
                StartSelfReviewQuickScore,
                dismissAction: () => m_selfReviewPromptActive = false);
            SaveSettings();
        }

        private void ShowToastPrompt(
            string message,
            Action confirmAction = null,
            Action closeAction = null,
            bool showClose = true,
            double autoHideSeconds = 0.0,
            string confirmText = null,
            string closeText = null,
            Action dismissAction = null)
        {
            if (m_selfReviewPromptActive)
            {
                HideSelfReviewPrompt();
            }

            m_selfReviewPromptActive = true;
            CommunityToastManager.Show(
                message,
                confirmAction,
                closeAction,
                showClose,
                autoHideSeconds,
                confirmText,
                closeText,
                () =>
                {
                    m_selfReviewPromptActive = false;
                    dismissAction?.Invoke();
                });
        }

        private void HideSelfReviewPrompt()
        {
            m_selfReviewPromptActive = false;
            CommunityToastManager.Hide();
        }

        private void StartSelfReviewQuickScore()
        {
            int cachedPostId = GetCachedSelfReviewPostId();
            double playSeconds = GetSelfReviewPlaySeconds();
            StartSelfScore(
                ModIdentity.PackageName,
                cachedPostId,
                SelfReviewFileName,
                playSeconds,
                5,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "SelfReview", "Useful"),
                ModIdentity.PackageName,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "SelfReview", "ThanksForYourSupport"),
                showSubmittingDialog: false);
        }

        private readonly ReviewSubmissionHandler m_selfScoreHandler;

        // 社区屏自评分工件：独立于本地屏评分工件（本地屏 m_reviewHandler）。
        private void StartSelfScore(
            string key,
            int cachedPostId,
            string fileName,
            double playSeconds,
            int rating,
            string text,
            string packageName = null,
            string successToastMessage = null,
            bool showSubmittingDialog = true)
        {
            m_selfScoreHandler.TrySubmit(new ReviewSubmissionRequest
            {
                ResourceKey = key,
                CachedPostId = cachedPostId,
                FileName = fileName,
                PackageName = packageName,
                PlaySeconds = playSeconds,
                Rating = rating,
                Content = text,
                SuccessToastMessage = successToastMessage,
                ShowSubmittingDialog = showSubmittingDialog
            });
        }

        private void UpdateSelfScoreTask()
        {
            m_selfScoreHandler.TryTakeResult();
        }

        private int GetCachedSelfReviewPostId()
        {
            string[] keys =
            [
                ModIdentity.PackageName,
                ModIdentity.DisplayName,
                SelfReviewFileName,
                PathWithoutExtension(SelfReviewFileName)
            ];
            foreach (string key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && m_localModReviews.TryGetValue(key, out LocalModReviewEntry entry)
                    && entry?.PostId > 0)
                {
                    return entry.PostId;
                }
            }
            return 0;
        }

        private static double GetSelfReviewPlaySeconds()
        {
            ModEntity entity = ModsManager.ModList.FirstOrDefault(mod =>
                mod?.modInfo != null
                && string.Equals(mod.modInfo.PackageName, ModIdentity.PackageName, StringComparison.OrdinalIgnoreCase));
            return entity == null ? 0.0 : PlayedTimeTracker.GetTotalSeconds(entity);
        }

        private static string PathWithoutExtension(string value)
        {
            int dotIndex = string.IsNullOrWhiteSpace(value) ? -1 : value.LastIndexOf('.');
            return dotIndex <= 0 ? value : value[..dotIndex];
        }
    }
}
