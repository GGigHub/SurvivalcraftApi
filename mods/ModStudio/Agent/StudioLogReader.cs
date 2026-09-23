using System.Collections.Concurrent;
using Engine;

namespace Game {
    public static class StudioLogReader {
        const int MaxLines = 200;
        static readonly ConcurrentQueue<string> s_lines = new();

        public static void Append(string line) {
            if (string.IsNullOrEmpty(line)) {
                return;
            }
            s_lines.Enqueue(line);
            while (s_lines.Count > MaxLines && s_lines.TryDequeue(out string _)) {
            }
        }

        public static string Tail(int maxLines) {
            int take = maxLines < 1 ? 1 : Math.Min(maxLines, 2000);
            List<string> chunks = [];
            string gameLog = TryReadGameLog(take);
            if (!string.IsNullOrEmpty(gameLog)) {
                chunks.Add("----- Game.log -----");
                chunks.Add(gameLog);
            }
            string[] studio = s_lines.ToArray();
            if (studio.Length > 0) {
                chunks.Add("----- ModStudio -----");
                int start = Math.Max(0, studio.Length - take);
                for (int i = start; i < studio.Length; i++) {
                    chunks.Add(studio[i]);
                }
            }
            return chunks.Count == 0 ? "" : string.Join("\n", chunks);
        }

        static string TryReadGameLog(int tailLines) {
            try {
                string path = Storage.CombinePaths(ModsManager.LogPath, "Game.log");
                if (!Storage.FileExists(path)) {
                    return "";
                }
                string systemPath = Storage.GetSystemPath(path);
                using FileStream stream = new(systemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new(stream);
                string text = reader.ReadToEnd();
                if (string.IsNullOrEmpty(text)) {
                    return "";
                }
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                int start = Math.Max(0, lines.Length - tailLines);
                return string.Join("\n", lines, start, lines.Length - start);
            }
            catch (Exception e) {
                return $"(failed to read Game.log: {e.Message})";
            }
        }
    }
}
