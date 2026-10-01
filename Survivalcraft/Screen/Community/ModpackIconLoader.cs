using Engine.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Game {
    sealed class ModpackIconLoadResult {
        public string Key { get; init; }
        public Texture2D Texture { get; init; }
        public string Error { get; init; }
    }

    /// <summary>
    /// Loads integration-pack icons without retaining UI widgets. Texture creation is
    /// performed when the screen consumes a completed result on the game thread.
    /// </summary>
    sealed class ModpackIconLoader {
        // 缓存对齐本体 EvictableTextureCache（Screen/Community/EvictableTextureCache.cs），
        // Add 返回被驱逐键列表，此处与模组一致不消费返回值（退休纹理由 DisposeRetired 统一释放）。
        readonly EvictableTextureCache m_cache = new(64, StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> m_missing = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Task<DecodedIcon>> m_tasks = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGetCachedTexture(string key, out Texture2D texture) => m_cache.TryGetValue(key, out texture);
        public bool IsMissing(string key) => !string.IsNullOrWhiteSpace(key) && m_missing.Contains(key);

        public void MarkMissing(string key) {
            if (!string.IsNullOrWhiteSpace(key)) {
                m_missing.Add(key);
            }
        }

        public void AddTexture(string key, Texture2D texture) {
            if (!string.IsNullOrWhiteSpace(key)
                && texture != null) {
                m_cache.Add(key, texture);
            }
        }

        public bool TryStartEmbedded(string key, byte[] bytes) {
            if (string.IsNullOrWhiteSpace(key)
                || bytes == null
                || bytes.Length == 0
                || IsMissing(key)
                || m_cache.TryGetValue(key, out _)) {
                return false;
            }
            return Start(key, () => Decode(bytes));
        }

        public bool TryStartRemote(string url) {
            if (string.IsNullOrWhiteSpace(url)
                || IsMissing(url)
                || m_cache.TryGetValue(url, out _)) {
                return false;
            }
            return Start(
                url,
                async () => {
                    // 已核实取用方式：TransferManager.CommunityHttpDownloader（静态单例字段，
                    // ModsManager/Community/Transfers/TransferManager.cs），签名同模组 HttpDownloader.DownloadToMemoryAsync(url, null, ct)。
                    using MemoryStream downloaded = await TransferManager.CommunityHttpDownloader
                        .DownloadToMemoryAsync(url, null, CancellationToken.None)
                        .ConfigureAwait(false);
                    byte[] bytes = downloaded.ToArray();
                    if (bytes.Length == 0
                        || bytes.Length > 2 * 1024 * 1024) {
                        return new DecodedIcon { Key = url, Error = "empty or oversized icon" };
                    }
                    return await Task.Run(() => Decode(bytes)).ConfigureAwait(false);
                }
            );
        }

        public bool TryTakeCompleted(out ModpackIconLoadResult result) {
            result = null;
            KeyValuePair<string, Task<DecodedIcon>> completed = default;
            foreach (KeyValuePair<string, Task<DecodedIcon>> pair in m_tasks) {
                if (pair.Value.IsCompleted) {
                    completed = pair;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(completed.Key)) {
                return false;
            }
            m_tasks.Remove(completed.Key);
            DecodedIcon decoded;
            try {
                decoded = completed.Value.Result;
            }
            catch (Exception ex) {
                decoded = new DecodedIcon { Key = completed.Key, Error = ex.Message };
            }
            try {
                if (decoded?.Image == null) {
                    m_missing.Add(completed.Key);
                    result = new ModpackIconLoadResult { Key = completed.Key, Error = decoded?.Error };
                    return true;
                }
                Texture2D texture = Texture2D.Load(decoded.Image);
                m_cache.Add(completed.Key, texture);
                result = new ModpackIconLoadResult { Key = completed.Key, Texture = texture };
            }
            catch (Exception ex) {
                m_missing.Add(completed.Key);
                result = new ModpackIconLoadResult { Key = completed.Key, Error = ex.Message };
            }
            finally {
                decoded?.Image?.Dispose();
            }
            return true;
        }

        public void DisposeRetired() => m_cache.DisposeRetired();

        public void ClearPending() {
            foreach (Task<DecodedIcon> task in m_tasks.Values) {
                _ = task.ContinueWith(
                    t => {
                        if (t.IsCompletedSuccessfully) {
                            t.Result?.Image?.Dispose();
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
            m_tasks.Clear();
        }

        internal static string MakeEmbeddedKey(string packPath, string iconPath) {
            string path = NormalizeStoragePath(packPath);
            string icon = NormalizeStoragePath(iconPath);
            return string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(icon) ? null : $"embedded:{path}|{icon}";
        }

        bool Start(string key, Func<Task<DecodedIcon>> load) {
            if (m_tasks.ContainsKey(key)) {
                return false;
            }
            m_tasks[key] = Task.Run(load);
            return true;
        }

        static async Task<DecodedIcon> Decode(byte[] bytes) {
            // 已核实：本体 PostIconDecoder 公开面为 Decode(byte[])（Game/Community/Infrastructure/Http/PostIconDecoder.cs），
            // 无 DecodeStandalone，此即其独立图解码入口（静态无状态）；Task.Run 后台解码，
            // Texture2D.Load 由调用方在游戏线程 Update 轮询 TryTakeCompleted 时执行（模组原机制）。
            try {
                return new DecodedIcon { Image = await Task.Run(() => PostIconDecoder.Decode(bytes)).ConfigureAwait(false) };
            }
            catch (Exception ex) {
                return new DecodedIcon { Error = ex.Message };
            }
        }

        static string NormalizeStoragePath(string value) {
            string normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal)) {
                normalized = normalized[2..];
            }
            while (normalized.Contains("//", StringComparison.Ordinal)) {
                normalized = normalized.Replace("//", "/");
            }
            return normalized.Trim('/');
        }

        sealed class DecodedIcon {
            public string Key { get; init; }
            public Image<Rgba32> Image { get; init; }
            public string Error { get; init; }
        }
    }
}