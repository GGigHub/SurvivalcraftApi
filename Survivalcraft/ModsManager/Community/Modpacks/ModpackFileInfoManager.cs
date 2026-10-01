namespace Game {
    // 已核实本体取用方式：TransferManager.ModpackSourceResolver（internal static readonly，
    // ModsManager/Community/Transfers/TransferManager.cs），实现 IModpackSourceResolver，构造注入该接口。
    sealed class ModpackFileInfoManager {
        const int CacheCapacity = 512;
        readonly IModpackSourceResolver m_sourceResolver;
        readonly Lock m_cacheSync = new();
        readonly Dictionary<string, PostFileItem> m_cache = new(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> m_cacheOrder = new();

        public ModpackFileInfoManager(IModpackSourceResolver sourceResolver) =>
            m_sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

        public async Task<ModpackFileInfoLoadResult> LoadAsync(ModpackFileInfoLoadRequest request, CancellationToken cancellationToken = default) {
            if (request == null) {
                throw new ArgumentNullException(nameof(request));
            }

            // 限流并发请求，大整合包瞬时全量 /upload/info 会打爆服务端。
            using SemaphoreSlim limiter = new(6);
            Task<ModpackFileInfoEntry>[] tasks = [.. (request.FileIds ?? []).Select(async fileId => {
                        // ReSharper disable once AccessToDisposedClosure — limiter 由 using 释放，但下方 WhenAll 先行等待全部请求。
                        await limiter.WaitAsync(cancellationToken);
                        try {
                            return await LoadOneAsync(request.ApiBaseUrl, request.Token, fileId, cancellationToken);
                        }
                        finally {
                            // ReSharper disable once AccessToDisposedClosure
                            limiter.Release();
                        }
                    }
                )
                ];
            return new ModpackFileInfoLoadResult {
                RequestId = request.RequestId,
                ResourcePath = request.ResourcePath,
                ApiBaseUrl = request.ApiBaseUrl,
                Entries = await Task.WhenAll(tasks)
            };
        }

        public bool TryGetCached(string apiBaseUrl, string fileId, out PostFileItem file) {
            lock (m_cacheSync) {
                return m_cache.TryGetValue(MakeCacheKey(apiBaseUrl, fileId), out file);
            }
        }

        public void Cache(string apiBaseUrl, string fileId, PostFileItem file) {
            if (file == null
                || string.IsNullOrWhiteSpace(fileId)) {
                return;
            }
            string key = MakeCacheKey(apiBaseUrl, fileId);
            lock (m_cacheSync) {
                // 已存在的键只更新值不入队，避免同键重复入队导致弹出时误删新条目。
                if (!m_cache.TryAdd(key, file)) {
                    m_cache[key] = file;
                    return;
                }
                m_cacheOrder.Enqueue(key);
                while (m_cacheOrder.Count > CacheCapacity) {
                    m_cache.Remove(m_cacheOrder.Dequeue());
                }
            }
        }

        internal static string MakeCacheKey(string apiBaseUrl, string fileId) =>
            $"{apiBaseUrl?.TrimEnd('/') ?? string.Empty}|{fileId?.Trim() ?? string.Empty}";

        async Task<ModpackFileInfoEntry> LoadOneAsync(string apiBaseUrl, string token, string fileId, CancellationToken cancellationToken) {
            try {
                PostFileItem file = await m_sourceResolver.FetchFileInfoByIdAsync(apiBaseUrl, token, fileId, cancellationToken);
                return new ModpackFileInfoEntry { FileId = fileId, File = file };
            }
            catch (Exception ex) {
                return new ModpackFileInfoEntry { FileId = fileId, Error = ex.Message };
            }
        }
    }

    sealed class ModpackFileInfoLoadRequest {
        public int RequestId { get; set; }
        public string ResourcePath { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public IReadOnlyList<string> FileIds { get; set; }
    }

    sealed class ModpackFileInfoLoadResult {
        public int RequestId { get; set; }
        public string ResourcePath { get; set; }
        public string ApiBaseUrl { get; set; }
        public IReadOnlyList<ModpackFileInfoEntry> Entries { get; set; } = [];
    }

    sealed class ModpackFileInfoEntry {
        public string FileId { get; set; }
        public PostFileItem File { get; set; }
        public string Error { get; set; }
    }
}