using Engine.Graphics;

namespace Game {
    /// <summary>
    /// 有容量上限的纹理缓存。被覆写/驱逐的纹理不立即 Dispose（可见节点可能仍引用），
    /// 移入退休队列，由调用方在旧节点销毁（树/列表 Clear）后调 DisposeRetired 统一释放。
    /// </summary>
    sealed class EvictableTextureCache {
        readonly Dictionary<string, Texture2D> m_textures;
        readonly Queue<string> m_insertionOrder;
        readonly List<Texture2D> m_retired;
        readonly int m_capacity;

        public EvictableTextureCache(int capacity, StringComparer comparer) {
            m_capacity = Math.Max(capacity, 1);
            m_textures = new Dictionary<string, Texture2D>(comparer);
            m_insertionOrder = new Queue<string>();
            m_retired = [];
        }

        public bool TryGetValue(string key, out Texture2D texture) => m_textures.TryGetValue(key, out texture);

        /// <summary>写入并返回被驱逐的键（若无驱逐返回 null）。</summary>
        public List<string> Add(string key, Texture2D texture) {
            List<string> evictedKeys = null;
            if (m_textures.TryGetValue(key, out Texture2D existing)) {
                // 覆写：旧纹理移入退休队列，由调用方在安全时机 Dispose。
                m_retired.Add(existing);
            }
            else {
                m_insertionOrder.Enqueue(key);
                if (m_insertionOrder.Count > m_capacity) {
                    string evictedKey = m_insertionOrder.Dequeue();
                    if (m_textures.Remove(evictedKey, out Texture2D evicted)) {
                        m_retired.Add(evicted);
                        evictedKeys = [evictedKey];
                    }
                }
            }
            m_textures[key] = texture;
            return evictedKeys;
        }

        public void DisposeRetired() {
            foreach (Texture2D texture in m_retired) {
                texture?.Dispose();
            }
            m_retired.Clear();
        }
    }
}