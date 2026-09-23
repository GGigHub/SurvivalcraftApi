namespace Game {
    public sealed class StudioRoute {
        public string ToolName;
        public string PackageName;
        public bool NeedsAuth = true;
        public bool IsHealth;
    }

    public static class StudioHttpRouter {
        public static bool TryMatch(string method, string path, out StudioRoute route) {
            route = null;
            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path)) {
                return false;
            }
            string verb = method.ToUpperInvariant();
            string normalized = Normalize(path);
            if (verb == "GET" && normalized == "/health") {
                route = new StudioRoute { IsHealth = true, NeedsAuth = false, ToolName = "health" };
                return true;
            }
            if (normalized == "/v1/tools") {
                if (verb == "GET") {
                    route = new StudioRoute { ToolName = "tools.catalog" };
                    return true;
                }
                return false;
            }
            if (normalized == "/v1/tools/call" && verb == "POST") {
                route = new StudioRoute { ToolName = "tools.call" };
                return true;
            }
            if (normalized == "/v1/mods") {
                if (verb == "GET") {
                    route = new StudioRoute { ToolName = "mod.list" };
                    return true;
                }
                if (verb == "POST") {
                    route = new StudioRoute { ToolName = "mod.create" };
                    return true;
                }
                return false;
            }
            if (normalized == "/v1/game/reload" && verb == "POST") {
                route = new StudioRoute { ToolName = "game.reload" };
                return true;
            }
            if (normalized == "/v1/game/log" && verb == "GET") {
                route = new StudioRoute { ToolName = "game.log" };
                return true;
            }
            const string modsPrefix = "/v1/mods/";
            if (!normalized.StartsWith(modsPrefix, StringComparison.Ordinal)) {
                return false;
            }
            string rest = normalized.Substring(modsPrefix.Length);
            if (string.IsNullOrEmpty(rest)) {
                return false;
            }
            string[] suffixes = ["/files", "/file", "/compile", "/pack", "/install", "/uninstall"];
            string suffix = null;
            for (int i = 0; i < suffixes.Length; i++) {
                if (rest.EndsWith(suffixes[i], StringComparison.Ordinal)) {
                    suffix = suffixes[i];
                    break;
                }
            }
            string package = suffix == null ? rest : rest.Substring(0, rest.Length - suffix.Length);
            if (string.IsNullOrEmpty(package) || package.Contains('/')) {
                return false;
            }
            string tool = MatchModTool(verb, suffix);
            if (tool == null) {
                return false;
            }
            route = new StudioRoute { ToolName = tool, PackageName = Uri.UnescapeDataString(package) };
            return true;
        }

        static string MatchModTool(string verb, string suffix) {
            if (suffix == null) {
                if (verb == "GET") {
                    return "mod.get";
                }
                if (verb == "DELETE") {
                    return "mod.delete_workspace";
                }
                return null;
            }
            return suffix switch {
                "/files" when verb == "GET" => "fs.list",
                "/file" when verb == "GET" => "fs.read",
                "/file" when verb == "PUT" => "fs.write",
                "/compile" when verb == "POST" => "mod.compile",
                "/pack" when verb == "POST" => "mod.pack",
                "/install" when verb == "POST" => "mod.install",
                "/uninstall" when verb == "POST" => "mod.uninstall",
                _ => null
            };
        }

        public static string Normalize(string path) {
            if (string.IsNullOrEmpty(path)) {
                return "/";
            }
            int query = path.IndexOf('?');
            string raw = query >= 0 ? path.Substring(0, query) : path;
            if (raw.Length > 1 && raw.EndsWith('/')) {
                raw = raw.TrimEnd('/');
            }
            if (!raw.StartsWith('/')) {
                raw = "/" + raw;
            }
            return raw;
        }
    }
}
