using System.Collections.Generic;

namespace Game
{
    internal sealed class PendingLocalModInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string PackageName { get; set; }
        public string Author { get; set; }
        public string Link { get; set; }
        public string ApiVersion { get; set; }
        public string ScVersion { get; set; }
        public IReadOnlyList<ModDependencyInfo> Dependencies { get; set; } = [];
    }
}
