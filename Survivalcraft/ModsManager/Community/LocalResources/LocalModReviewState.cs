using System;
using System.Collections.Generic;
using Engine;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// 本地资源评价缓存的进程级存储：按资源 key / 显示名 / 文件名多键映射评价与已发布帖子 ID，
    /// 让导入后、改名后、重开页面后都更容易复用历史关联。落盘经 LocalModReviewCache。
    /// </summary>
    internal static class LocalModReviewState
    {
        private static readonly LocalModReviewCache s_reviewCache = new();
        private static bool s_reviewsLoaded;

        public static Dictionary<string, LocalModReviewEntry> LocalModReviews
        {
            get
            {
                EnsureReviewsLoaded();
                return s_reviewCache.Entries;
            }
        }

        public static void EnsureReviewsLoaded()
        {
            if (s_reviewsLoaded)
            {
                return;
            }
            s_reviewsLoaded = true;
            try
            {
                Exception error = s_reviewCache.Load();
                if (error != null)
                {
                    throw error;
                }
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalModReviewState", "ReadReviewsFailed"), ex.Message));
            }
        }

        public static void SaveLocalModReviews()
        {
            try
            {
                Exception error = s_reviewCache.Save();
                if (error != null)
                {
                    throw error;
                }
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalModReviewState", "SaveReviewsFailed"), ex.Message));
            }
        }

        public static int GetCachedLocalModPostId(LocalResourceItem resourceItem)
        {
            string filePath = TransferManager.UploadFileProvider.GetLocalResourceFilePath(resourceItem);
            string fileName = GameUploadFileProvider.GetUploadFileName(resourceItem, filePath);
            string[] keys =
            [
                LocalResourceIdentity.MakeModReviewKey(resourceItem),
                resourceItem?.DisplayName,
                resourceItem?.Name,
                fileName,
                FileNameRules.GetFileNameWithoutExtension(fileName)
            ];
            foreach (string key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && LocalModReviews.TryGetValue(key, out LocalModReviewEntry entry)
                    && entry?.PostId > 0)
                {
                    return entry.PostId;
                }
            }
            return 0;
        }

        public static void RememberLocalModPostId(string importedName, string fileName, int postId)
        {
            RememberLocalPostId(null, importedName, fileName, postId);
        }

        public static void RememberLocalPostId(string resourceKey, string displayName, string fileName, int postId)
        {
            if (postId <= 0)
            {
                return;
            }
            string[] keys =
            [
                resourceKey,
                displayName,
                fileName,
                FileNameRules.GetFileNameWithoutExtension(fileName)
            ];
            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }
                if (!LocalModReviews.TryGetValue(key, out LocalModReviewEntry entry) || entry == null)
                {
                    entry = new LocalModReviewEntry();
                }
                entry.PostId = postId;
                LocalModReviews[key] = entry;
            }
            SaveLocalModReviews();
        }
    }
}
