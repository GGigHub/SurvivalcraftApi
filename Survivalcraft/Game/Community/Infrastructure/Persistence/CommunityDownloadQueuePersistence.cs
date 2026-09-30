using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Engine;

namespace Game
{
    internal sealed class CommunityDownloadQueuePersistence
    {
        public DownloadQueuePersistedState Load()
        {
            return CommunityDownloadQueueStore.Load();
        }

        public void Save(IEnumerable<CommunityDownloadQueueItem> items, int nextId)
        {
            CommunityDownloadQueueStore.Save(new DownloadQueuePersistedState
            {
                Version = 1,
                NextId = nextId,
                Items = (items ?? []).Select(ToStored).Where(item => item != null).ToArray()
            });
        }

        public CommunityDownloadQueueItem Restore(DownloadQueueStoredItem stored)
        {
            if (stored == null || string.IsNullOrWhiteSpace(stored.Url) || string.IsNullOrWhiteSpace(stored.FileName))
            {
                return null;
            }

            int id = Math.Max(1, stored.Id);
            CommunityDownloadQueueItem item = new()
            {
                Id = id,
                Url = stored.Url,
                OriginalUrl = string.IsNullOrWhiteSpace(stored.OriginalUrl) ? stored.Url : stored.OriginalUrl,
                FileName = stored.FileName,
                Type = stored.Type,
                RequiresRestart = stored.RequiresRestart,
                FeedbackInfo = stored.FeedbackInfo,
                ReplaceExistingScbbsPlusMod = stored.ReplaceExistingScbbsPlusMod,
                ReplaceExistingModPackageName = stored.ReplaceExistingModPackageName,
                ReplaceExistingModPath = stored.ReplaceExistingModPath,
                OverwriteExistingFilePath = stored.OverwriteExistingFilePath,
                TempFilePath = IsTrustedTempFilePath(id, stored.TempFilePath)
                    ? stored.TempFilePath
                    : CreateTempFilePath(id, stored.FileName),
                // 按名恢复下载源；内置内部名沿用旧枚举名，旧队列文件字符串值直查命中，未命中回退默认线路
                Source = CommunityDownloadSourceManager.FindByName(stored.Source) ?? CommunityDownloadSourceManager.DefaultCnInfo,
                State = CommunityDownloadQueueStateMachine.NormalizeLoaded(stored.State),
                CreatedTime = stored.CreatedTime == default ? DateTime.Now : stored.CreatedTime,
                StartedTime = stored.StartedTime,
                CompletedTime = stored.CompletedTime,
                Error = stored.Error,
                Imported = stored.Imported,
                Progress = new CommunityTransferProgress()
            };
            if (item.Imported)
            {
                // 导入成功后、收尾落盘前进程死亡：磁盘 State 停在 Running（上方归一为 Paused），
                // 但导入实际已完成。改判 Completed 终态，恢复流程不得再触发导入。
                item.State = CommunityDownloadQueueItemState.Completed;
                // 该态永不再续传/导入，.part 缓存不会再被成功路径清理，顺手删除。
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.TempFilePath) && Storage.FileExists(item.TempFilePath))
                    {
                        Storage.DeleteFile(item.TempFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadQueuePersistence", "CleanImportedItemCacheFailed"), item.TempFilePath, ex.Message));
                }
            }
            return item;
        }

        public static string CreateTempFilePath(int id, string fileName)
        {
            // 引擎 GetFileName 纯字符串操作、null 安全返回 Empty，无消毒失败路径；
            // 空白兜底 download_{id} 由下方检查承担。
            string safeFileName = Storage.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = $"download_{id}";
            }
            safeFileName = CommunityUploadFileNaming.SanitizeFileName(safeFileName);
            return Storage.CombinePaths(CommunityStoragePaths.DownloadCacheDirectory, $"{id}_{safeFileName}.part");
        }

        private static CommunityDownloadFeedbackInfo StripToken(CommunityDownloadFeedbackInfo info)
        {
            if (info == null)
            {
                return null;
            }
            // 队列文件不走认证存储的加密通道，明文 Token 不得落盘：
            // 登出只清认证文件，云同步会带走整个游戏目录。克隆剥离，运行时对象不动
            // （同会话上报仍用快照）；恢复项由反馈客户端按当前登录态补取。
            return new CommunityDownloadFeedbackInfo
            {
                ApiBaseUrl = info.ApiBaseUrl,
                Token = null,
                PostId = info.PostId,
                FileId = info.FileId,
                Platform = info.Platform,
                Version = info.Version
            };
        }

        private static DownloadQueueStoredItem ToStored(CommunityDownloadQueueItem item)
        {
            if (item == null)
            {
                return null;
            }

            return new DownloadQueueStoredItem
            {
                Id = item.Id,
                Url = item.Url,
                OriginalUrl = item.OriginalUrl,
                FileName = item.FileName,
                Type = item.Type,
                RequiresRestart = item.RequiresRestart,
                FeedbackInfo = StripToken(item.FeedbackInfo),
                ReplaceExistingScbbsPlusMod = item.ReplaceExistingScbbsPlusMod,
                ReplaceExistingModPackageName = item.ReplaceExistingModPackageName,
                ReplaceExistingModPath = item.ReplaceExistingModPath,
                OverwriteExistingFilePath = item.OverwriteExistingFilePath,
                TempFilePath = item.TempFilePath,
                Source = item.Source?.Name,
                State = CommunityDownloadQueueStateMachine.NormalizeStored(item.State),
                CreatedTime = item.CreatedTime,
                StartedTime = item.StartedTime,
                CompletedTime = item.CompletedTime,
                Error = item.Error,
                Imported = item.Imported
            };
        }

        private static bool IsTrustedTempFilePath(int id, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            try
            {
                // 恢复自持久化文件：内部缓存路径只接受位于下载缓存目录内、且形态为
                // {id}_*.part 的路径。手工编辑队列文件/跨机器拷贝可能指向任意路径，
                // 下载器按续传语义会对该路径读+写（截断写入任意文件），必须重建兜底。
                if (!string.Equals(
                    Storage.GetDirectoryName(path),
                    CommunityStoragePaths.DownloadCacheDirectory,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                string fileName = Storage.GetFileName(path);
                return fileName.StartsWith($"{id}_", StringComparison.Ordinal)
                    && fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // 恶意/畸形路径抛异常按不可信处理，走重建。
                return false;
            }
        }
    }
}
