using Engine;

namespace Game {
    sealed class CommunityModVersionDisplayData {
        public PostVersionItem Version { get; init; }

        public string VersionKey { get; init; }

        public string VersionTitle { get; init; }

        public string FileTitle { get; init; }

        public string CompatibilityText { get; init; }

        public Color TitleColor { get; init; }

        public Color CompatibilityColor { get; init; }

        public bool IsCurrent { get; init; }

        public bool HasSingleFile { get; init; }

        public bool HasExpandableFiles { get; init; }

        public IReadOnlyList<CommunityModFileDisplayData> Files { get; init; } = [];
    }

    sealed class CommunityModFileDisplayData {
        public PostFileItem File { get; init; }

        public string FileTitle { get; init; }

        public string DownloadsSizeText { get; init; }
    }
}