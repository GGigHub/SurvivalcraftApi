using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;

namespace Game
{
    /// <summary>
    /// 整合包图标加载协调器（自模组 ScbbsPlusLocalScreen.Modpacks.Page.cs 提取）：
    /// 持有"等待加载完成"的图标 widget 名册与已装/缺失判定，屏侧只转发。
    /// loader 由屏注入（静态共享缓存语义保持不变）。
    /// </summary>
    internal sealed class ModpackIconManager
    {
        private readonly ModpackIconLoader m_loader;
        private readonly Dictionary<string, List<RectangleWidget>> m_waiters = new(StringComparer.OrdinalIgnoreCase);

        public ModpackIconManager(ModpackIconLoader loader)
        {
            m_loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>清单内嵌图标在缓存中的已装贴图；未命中返回 null（调用方用占位图标）。</summary>
        public Texture2D GetCachedEmbeddedTexture(LocalResourceItem item, LocalModpackRecord record)
        {
            string iconPath = record?.Manifest?.Icon?.Trim();
            if (string.IsNullOrWhiteSpace(iconPath) || record.IconData == null || record.IconData.Length == 0)
            {
                return null;
            }

            string cacheKey = MakeCacheKey(item?.LocalFilePath, iconPath);
            if (!string.IsNullOrWhiteSpace(cacheKey))
            {
                if (m_loader.TryGetCachedTexture(cacheKey, out Texture2D cachedTexture))
                {
                    return cachedTexture;
                }
            }
            return null;
        }

        /// <summary>图标加载统一入口：嵌入优先，嵌入路径未接管（无内嵌数据/嵌入已判缺失）才轮到远程，
        /// 避免同一 widget 以嵌入与远程两个 key 双注册、双方完成顺序互相覆盖。</summary>
        public void StartIconRequests(LocalResourceItem item, LocalModpackRecord record, RectangleWidget iconWidget)
        {
            if (!StartEmbeddedRequest(item, record, iconWidget))
            {
                StartRemoteRequest(item, record, iconWidget);
            }
        }

        /// <summary>发起内嵌图标加载；完成后的贴图经 Update() 分发给等待 widget（含已被滚动分离的实例）。
        /// 返回是否已接管该 widget（缓存命中已应用、已注册 waiter 或嵌入终态）。</summary>
        public bool StartEmbeddedRequest(LocalResourceItem item, LocalModpackRecord record, RectangleWidget iconWidget)
        {
            string iconPath = record?.Manifest?.Icon?.Trim();
            if (iconWidget == null || string.IsNullOrWhiteSpace(iconPath) || record?.IconData == null || record.IconData.Length == 0)
            {
                return false;
            }

            string cacheKey = MakeCacheKey(item?.LocalFilePath, iconPath);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return false;
            }
            if (m_loader.IsMissing(cacheKey))
            {
                return false;
            }
            if (m_loader.TryGetCachedTexture(cacheKey, out Texture2D cachedTexture))
            {
                iconWidget.Subtexture = new Subtexture(cachedTexture, Vector2.Zero, Vector2.One);
                return true;
            }

            if (!m_waiters.TryGetValue(cacheKey, out List<RectangleWidget> waiters))
            {
                waiters = [];
                m_waiters[cacheKey] = waiters;
            }
            if (!waiters.Contains(iconWidget))
            {
                waiters.Add(iconWidget);
            }
            m_loader.TryStartEmbedded(cacheKey, record.IconData);
            return true;
        }

        /// <summary>发起远程图标加载（URL 即缓存键，loader 内部去重）；完成后的贴图经 Update() 分发给等待 widget（含已被滚动分离的实例）。</summary>
        public void StartRemoteRequest(LocalResourceItem item, LocalModpackRecord record, RectangleWidget iconWidget)
        {
            string iconUrl = ResolveRemoteIconUrl(item, record);
            if (string.IsNullOrWhiteSpace(iconUrl) || iconWidget == null)
            {
                return;
            }

            if (m_loader.TryGetCachedTexture(iconUrl, out Texture2D cachedTexture))
            {
                iconWidget.Subtexture = new Subtexture(cachedTexture, Vector2.Zero, Vector2.One);
                return;
            }
            if (m_loader.IsMissing(iconUrl))
            {
                return;
            }
            if (!m_waiters.TryGetValue(iconUrl, out List<RectangleWidget> waiters))
            {
                waiters = [];
                m_waiters[iconUrl] = waiters;
            }
            if (!waiters.Contains(iconWidget))
            {
                waiters.Add(iconWidget);
            }
            m_loader.TryStartRemote(iconUrl);
        }

        /// <summary>每帧轮询：加载完成后把贴图分发给等待 widget；已被滚动虚拟化分离的实例也即刻贴图，
        /// 引擎滚回重挂复用同一 widget（不重跑工厂），纹理已在身上即直接显示。结算即出册，名册只剩在途 key。</summary>
        public void Update()
        {
            if (m_loader.TryTakeCompleted(out ModpackIconLoadResult loaded))
            {
                ApplyLoaded(loaded);
            }
            ApplyReadyTexturesToWaiters();
        }

        /// <summary>屏重建/离场时清空等待名册（loader 的 ClearPending/DisposeRetired 仍由屏直接驱动）。</summary>
        public void Clear()
        {
            m_waiters.Clear();
        }

        private void ApplyLoaded(ModpackIconLoadResult result)
        {
            if (result?.Texture == null || !m_waiters.TryGetValue(result.Key, out List<RectangleWidget> waiters))
            {
                if (result != null) m_waiters.Remove(result.Key);
                return;
            }
            if (ApplyReadyTexture(result.Key, waiters, result.Texture))
            {
                m_waiters.Remove(result.Key);
            }
        }

        /// <summary>把已就绪贴图应用到全部 waiter（含滚动分离的实例）并出册：Subtexture 是纯字段赋值、
        /// 分离 widget 不参与绘制，先贴即等价于滚回补应用，结算后 key 即清空、名册不再滞留闲置条目。
        /// 恒返回 true（调用方删 key）。</summary>
        private bool ApplyReadyTexture(string key, List<RectangleWidget> waiters, Texture2D texture)
        {
            for (int i = waiters.Count - 1; i >= 0; i--)
            {
                RectangleWidget waiter = waiters[i];
                if (waiter != null)
                {
                    waiter.Subtexture = new Subtexture(texture, Vector2.Zero, Vector2.One);
                }
                waiters.RemoveAt(i);
            }
            return waiters.Count == 0;
        }

        /// <summary>兜底扫描：同帧第二个完成 key（TryTakeCompleted 每帧只取一个）等一帧即在此补应用，
        /// 也防御经结算之外路径进缓存的纹理。正常结算已在 ApplyReadyTexture 处清空对应 key，
        /// 名册平时只剩在途 key，扫描范围随之收敛。</summary>
        private void ApplyReadyTexturesToWaiters()
        {
            if (m_waiters.Count == 0)
            {
                return;
            }
            List<string> settledKeys = null;
            foreach (KeyValuePair<string, List<RectangleWidget>> pair in m_waiters)
            {
                if (m_loader.TryGetCachedTexture(pair.Key, out Texture2D texture)
                    && ApplyReadyTexture(pair.Key, pair.Value, texture))
                {
                    (settledKeys ??= []).Add(pair.Key);
                }
            }
            if (settledKeys == null)
            {
                return;
            }
            foreach (string key in settledKeys)
            {
                m_waiters.Remove(key);
            }
        }

        private static string ResolveRemoteIconUrl(LocalResourceItem item, LocalModpackRecord record)
        {
            string icon = record?.Manifest?.Icon?.Trim();
            if (string.IsNullOrWhiteSpace(icon))
            {
                return null;
            }
            if (ModpackRules.IsHttpUrl(icon))
            {
                return icon;
            }

            // ScbbsPlusFileNameRules.GetExtension → Storage.GetExtensionBCL（Engine/Storage.cs，
            // 点文件语义，先例映射 GetFileNameWithoutExtensionBCL）。
            if (string.Equals(Storage.GetExtensionBCL(item?.LocalFilePath ?? string.Empty), ".zip", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(record?.SourceUrl, UriKind.Absolute, out Uri sourceUri)
                || (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return Uri.TryCreate(sourceUri, icon.Replace('\\', '/'), out Uri resolvedUri)
                ? resolvedUri.AbsoluteUri
                : null;
        }

        private static string MakeCacheKey(string packPath, string iconPath)
        {
            // 本体无 PendingLocalModMetadataReader；ModsManager.NormalizeStoragePath（ModsManager.cs）
            // 与模组 ScbbsPlusPendingLocalModMetadataReader.NormalizeStoragePath 实现逐字一致（'\\'→'/' 后 Trim）。
            string normalizedPath = ModsManager.NormalizeStoragePath(packPath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            DateTime? lastWriteTime = ModpackRegistry.TryGetExistingFileLastWriteTime(packPath);
            return $"{normalizedPath}|{iconPath.Trim()}|{lastWriteTime?.Ticks ?? 0L}";
        }
    }
}
