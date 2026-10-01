namespace Game {
    sealed class LocalModReviewCache {
        public Dictionary<string, LocalModReviewEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception Load() {
            try {
                Entries.Clear();
                foreach (KeyValuePair<string, LocalModReviewEntry> pair in LocalModReviewStore.Load()) {
                    if (!string.IsNullOrWhiteSpace(pair.Key)
                        && pair.Value != null) {
                        Entries[pair.Key] = pair.Value;
                    }
                }
                return null;
            }
            catch (Exception ex) {
                return ex;
            }
        }

        public Exception Save() {
            try {
                LocalModReviewStore.Save(Entries);
                return null;
            }
            catch (Exception ex) {
                return ex;
            }
        }
    }
}