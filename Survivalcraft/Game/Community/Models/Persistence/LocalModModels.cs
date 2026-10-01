namespace Game {
    public class LocalModReviewsData {
        public Dictionary<string, LocalModReviewEntry> Reviews { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class LocalModReviewEntry {
        public int PostId { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; }
        public string LastText { get; set; }
        public string UpdatedAt { get; set; }
    }

    public class LocalModPendingActionsData {
        public Dictionary<string, LocalModPendingActionEntry> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class LocalModPendingActionEntry {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public string PackageName { get; set; }
        public string Version { get; set; }
        public LocalModPendingActionType ActionType { get; set; }
        public string UpdatedAt { get; set; }
    }

    public enum LocalModPendingActionType {
        None = 0,
        Delete = 1,
        Disable = 2,
        Enable = 3
    }
}