using System.Text.Json.Nodes;

namespace Game {
    public sealed class StudioProjectInfo {
        public int SchemaVersion = 1;
        public string PackageName;
        public string DisplayName;
        public string AssemblyName;
        public string CreatedUtc;
        public string LastCompileUtc;
        public bool LastCompileOk;
        public string LastPackUtc;
        public string LastInstallUtc;

        public string ManifestFileName => ".scmodstudio.json";

        public JsonObject ToJson() {
            return new JsonObject {
                ["schemaVersion"] = SchemaVersion,
                ["packageName"] = PackageName ?? "",
                ["displayName"] = DisplayName ?? "",
                ["assemblyName"] = AssemblyName ?? "",
                ["createdUtc"] = CreatedUtc ?? "",
                ["lastCompileUtc"] = LastCompileUtc,
                ["lastCompileOk"] = LastCompileOk,
                ["lastPackUtc"] = LastPackUtc,
                ["lastInstallUtc"] = LastInstallUtc
            };
        }

        public static StudioProjectInfo FromJson(JsonNode node) {
            StudioProjectInfo info = new();
            if (node is not JsonObject obj) {
                return info;
            }
            info.SchemaVersion = ReadInt(obj, "schemaVersion", 1);
            info.PackageName = ReadString(obj, "packageName");
            info.DisplayName = ReadString(obj, "displayName");
            info.AssemblyName = ReadString(obj, "assemblyName");
            info.CreatedUtc = ReadString(obj, "createdUtc");
            info.LastCompileUtc = ReadString(obj, "lastCompileUtc");
            info.LastCompileOk = ReadBool(obj, "lastCompileOk");
            info.LastPackUtc = ReadString(obj, "lastPackUtc");
            info.LastInstallUtc = ReadString(obj, "lastInstallUtc");
            return info;
        }

        static string ReadString(JsonObject obj, string name) {
            if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return null;
            }
            try {
                return node.GetValue<string>();
            }
            catch {
                string text = node.ToString();
                return text == "null" ? null : text;
            }
        }

        static int ReadInt(JsonObject obj, string name, int fallback) {
            if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return fallback;
            }
            try {
                return node.GetValue<int>();
            }
            catch {
                return int.TryParse(node.ToString(), out int parsed) ? parsed : fallback;
            }
        }

        static bool ReadBool(JsonObject obj, string name) {
            if (!obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return false;
            }
            try {
                return node.GetValue<bool>();
            }
            catch {
                return bool.TryParse(node.ToString(), out bool parsed) && parsed;
            }
        }
    }
}
