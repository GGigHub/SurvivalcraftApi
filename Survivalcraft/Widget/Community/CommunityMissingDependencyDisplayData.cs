using System.Collections.Generic;

namespace Game
{
    internal sealed class CommunityMissingDependencyFileDisplayData
    {
        public PostFileItem File { get; init; }

        public string FileTitle { get; init; }

        public string MetadataText { get; init; }
    }

    internal sealed class CommunityMissingDependencyGroupDisplayData
    {
        public string GroupKey { get; init; }

        public string GroupText { get; init; }

        public string ExpandedGroupText { get; init; }

        public IReadOnlyList<CommunityMissingDependencyFileDisplayData> Files { get; init; } = [];
    }

    internal sealed class CommunityMissingDependencyPackageDisplayData
    {
        public string Text { get; init; }

        public string DetailText { get; init; }
    }
}
