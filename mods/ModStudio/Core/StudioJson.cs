using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game {
    public static class StudioJson {
        public static readonly JsonSerializerOptions Options = new() {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static JsonObject ParseObject(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                return [];
            }
            JsonNode node = JsonNode.Parse(json);
            return node as JsonObject ?? [];
        }

        public static string GetString(JsonObject obj, string name, string fallback = null) {
            if (obj == null || !obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return fallback;
            }
            if (node is JsonValue value) {
                return value.ToString();
            }
            return fallback;
        }

        public static bool GetBool(JsonObject obj, string name, bool fallback = false) {
            if (obj == null || !obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return fallback;
            }
            try {
                return node.GetValue<bool>();
            }
            catch {
                string text = node.ToString();
                if (bool.TryParse(text, out bool parsed)) {
                    return parsed;
                }
                return fallback;
            }
        }

        public static int GetInt(JsonObject obj, string name, int fallback) {
            if (obj == null || !obj.TryGetPropertyValue(name, out JsonNode node) || node == null) {
                return fallback;
            }
            try {
                return node.GetValue<int>();
            }
            catch {
                return int.TryParse(node.ToString(), out int parsed) ? parsed : fallback;
            }
        }
    }
}
