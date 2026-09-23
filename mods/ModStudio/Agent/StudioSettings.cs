using System.Text.Json.Nodes;
using Engine;

namespace Game {
    public sealed class StudioSettings {
        public static StudioSettings Current { get; private set; } = new();

        public string ListenAddress = "127.0.0.1";
        public int Port = 17865;
        public string Token;
        public bool AgentEnabled = true;

        public static string StoragePath => Storage.CombinePaths(ModsManager.DocPath, "ModStudio.settings.json");

        public static void Load() {
            try {
                if (!Storage.FileExists(StoragePath)) {
                    Current = CreateDefault();
                    Save();
                    return;
                }
                using Stream stream = Storage.OpenFile(StoragePath, OpenFileMode.Read);
                using StreamReader reader = new(stream);
                JsonObject obj = StudioJson.ParseObject(reader.ReadToEnd());
                StudioSettings settings = new() {
                    ListenAddress = "127.0.0.1",
                    Port = StudioJson.GetInt(obj, "port", 17865),
                    Token = StudioJson.GetString(obj, "token"),
                    AgentEnabled = StudioJson.GetBool(obj, "agentEnabled", true)
                };
                if (settings.Port < 1 || settings.Port > 65535) {
                    settings.Port = 17865;
                }
                if (string.IsNullOrWhiteSpace(settings.Token)) {
                    settings.Token = Guid.NewGuid().ToString("N");
                }
                Current = settings;
                Save();
            }
            catch (Exception e) {
                Log.Error($"[ModStudio] Load settings failed: {e}");
                Current = CreateDefault();
            }
        }

        public static void Save() {
            try {
                if (!Storage.DirectoryExists(ModsManager.DocPath)) {
                    Storage.CreateDirectory(ModsManager.DocPath);
                }
                JsonObject obj = new() {
                    ["listenAddress"] = "127.0.0.1",
                    ["port"] = Current.Port,
                    ["token"] = Current.Token ?? "",
                    ["agentEnabled"] = Current.AgentEnabled
                };
                using Stream stream = Storage.OpenFile(StoragePath, OpenFileMode.Create);
                using StreamWriter writer = new(stream);
                writer.Write(obj.ToJsonString());
            }
            catch (Exception e) {
                Log.Error($"[ModStudio] Save settings failed: {e}");
            }
        }

        static StudioSettings CreateDefault() => new() {
            ListenAddress = "127.0.0.1",
            Port = 17865,
            Token = Guid.NewGuid().ToString("N"),
            AgentEnabled = true
        };

        public string BaseUrl => $"http://127.0.0.1:{Port}/";
    }
}
