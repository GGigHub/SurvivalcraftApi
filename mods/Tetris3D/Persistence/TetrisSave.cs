using System.Text.Json;
using Engine;

namespace Game {
    public sealed class TetrisSave {
        public int HighScore;
        public bool SoundEnabled = true;

        static string DirectoryPath => "app:Tetris3D";
        static string FilePath => Storage.CombinePaths(DirectoryPath, "save.json");

        public void Read() {
            try {
                if (!Storage.FileExists(FilePath)) {
                    return;
                }
                string json = Storage.ReadAllText(FilePath);
                SaveData data = JsonSerializer.Deserialize<SaveData>(json);
                if (data != null) {
                    HighScore = data.HighScore;
                    SoundEnabled = data.SoundEnabled;
                }
            }
            catch (Exception e) {
                Log.Warning($"Tetris3D: failed to read save: {e.Message}");
            }
        }

        public void Write() {
            try {
                if (!Storage.DirectoryExists(DirectoryPath)) {
                    Storage.CreateDirectory(DirectoryPath);
                }
                SaveData data = new() {
                    HighScore = HighScore,
                    SoundEnabled = SoundEnabled
                };
                Storage.WriteAllText(FilePath, JsonSerializer.Serialize(data));
            }
            catch (Exception e) {
                Log.Warning($"Tetris3D: failed to write save: {e.Message}");
            }
        }

        public class SaveData {
            public int HighScore { get; set; }
            public bool SoundEnabled { get; set; } = true;
        }
    }
}
