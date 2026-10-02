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

        // 依赖声明中的下载页链接，空则不显示链接行
        public string Link { get; init; }
    }
}