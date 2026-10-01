namespace Game {
    sealed class CommunityMissingDependencyFileDisplayData {
        public PostFileItem File { get; init; }

        public string FileTitle { get; init; }

        public string MetadataText { get; init; }
    }

    sealed class CommunityMissingDependencyGroupDisplayData {
        public string GroupKey { get; init; }

        public string GroupText { get; init; }

        public string ExpandedGroupText { get; init; }

        public IReadOnlyList<CommunityMissingDependencyFileDisplayData> Files { get; init; } = [];
    }

    sealed class CommunityMissingDependencyPackageDisplayData {
        public string Text { get; init; }

        public string DetailText { get; init; }
    }
}