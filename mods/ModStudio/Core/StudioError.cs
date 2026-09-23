using System.Text.Json.Nodes;

namespace Game {
    public sealed class StudioResult {
        public bool Ok;
        public string Code;
        public string Message;
        public JsonNode Data;

        public static StudioResult Success(JsonNode data = null) => new() {
            Ok = true,
            Data = data ?? new JsonObject()
        };

        public static StudioResult Fail(string code, string message) => new() {
            Ok = false,
            Code = code ?? "io_error",
            Message = message ?? "Unknown error."
        };

        public JsonObject ToJson() {
            JsonObject obj = new() { ["ok"] = Ok };
            if (Data != null) {
                obj["data"] = Data;
            }
            if (!Ok) {
                obj["error"] = new JsonObject {
                    ["code"] = Code ?? "io_error",
                    ["message"] = Message ?? ""
                };
            }
            return obj;
        }
    }
}
