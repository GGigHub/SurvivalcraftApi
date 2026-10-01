using System.Text.Json;
using Engine;

namespace Game {
    static class LocalModReviewStore {
        static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

        public static Dictionary<string, LocalModReviewEntry> Load() {
            if (!Storage.FileExists(CommunityStoragePaths.LocalModReviewsFile)) {
                return new Dictionary<string, LocalModReviewEntry>(StringComparer.OrdinalIgnoreCase);
            }
            LocalModReviewsData data = JsonFileStore.Read<LocalModReviewsData>(CommunityStoragePaths.LocalModReviewsFile, s_options);
            return data?.Reviews == null
                ? new Dictionary<string, LocalModReviewEntry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LocalModReviewEntry>(data.Reviews, StringComparer.OrdinalIgnoreCase);
        }

        public static void Save(IReadOnlyDictionary<string, LocalModReviewEntry> reviews) {
            JsonFileStore.Write(
                CommunityStoragePaths.LocalModReviewsFile,
                new LocalModReviewsData {
                    Reviews = new Dictionary<string, LocalModReviewEntry>(
                        reviews ?? new Dictionary<string, LocalModReviewEntry>(),
                        StringComparer.OrdinalIgnoreCase
                    )
                },
                s_options
            );
        }
    }
}