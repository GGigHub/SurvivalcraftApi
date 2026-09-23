using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 整合包在场判定用的本机资源快照（纯数据，自 PresenceManager 拆分独立文件）。
    /// 捕获与失效由 ModpackPresenceManager 负责。
    /// </summary>
    internal sealed class ModpackPresenceSnapshot
    {
        public HashSet<string> WorldKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FurnitureKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TextureKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SkinKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ModPackageKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ModFileKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<KeyValuePair<string, string>> LocalFileNames { get; } = [];
        public Dictionary<string, string> ModPackageFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
