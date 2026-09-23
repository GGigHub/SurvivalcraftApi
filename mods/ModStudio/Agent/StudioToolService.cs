using System.Text.Json.Nodes;
using Engine;

namespace Game {
    public static class StudioToolService {
        static readonly HashSet<string> s_textExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".cs", ".json", ".md", ".txt", ".csproj", ".xml", ".csv", ".cr", ".xdb", ".clo", ".props", ".targets"
        };

        public static StudioResult Call(string name, JsonObject arguments) {
            JsonObject args = arguments ?? [];
            try {
                StudioResult result = name switch {
                    "tools.catalog" => Catalog(),
                    "mod.list" => ListMods(),
                    "mod.create" => Create(args),
                    "mod.get" => Get(args),
                    "mod.delete_workspace" => DeleteWorkspace(args),
                    "fs.list" => ListFiles(args),
                    "fs.read" => ReadFile(args),
                    "fs.write" => WriteFile(args),
                    "mod.compile" => Compile(args),
                    "mod.pack" => Pack(args),
                    "mod.install" => Install(args),
                    "mod.uninstall" => Uninstall(args),
                    "game.reload" => Reload(args),
                    "game.log" => GameLog(args),
                    _ => StudioResult.Fail("not_supported", $"Unknown tool: {name}")
                };
                if (name != "game.log" && name != "tools.catalog") {
                    StudioLogReader.Append($"[{DateTime.Now:HH:mm:ss}] {name} => {(result.Ok ? "ok" : result.Code)}");
                    Log.Information($"[ModStudio] tool {name} {(result.Ok ? "ok" : result.Code)}");
                }
                return result;
            }
            catch (FileNotFoundException e) {
                return StudioResult.Fail("not_found", e.Message);
            }
            catch (InvalidOperationException e) {
                string code = e.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("Protected", StringComparison.OrdinalIgnoreCase)
                    ? "forbidden"
                    : e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                        ? "already_exists"
                        : "invalid_argument";
                return StudioResult.Fail(code, e.Message);
            }
            catch (ArgumentException e) {
                return StudioResult.Fail("invalid_argument", e.Message);
            }
            catch (Exception e) {
                Log.Error($"[ModStudio] tool {name} failed: {e}");
                return StudioResult.Fail("io_error", e.Message);
            }
        }

        static StudioResult Catalog() {
            JsonArray tools = [
                Tool("tools.catalog", "List available tools."),
                Tool("mod.list", "List workspace projects."),
                Tool("mod.create", "Create a blank mod workspace.", "displayName", "packageName?"),
                Tool("mod.get", "Get one workspace.", "packageName"),
                Tool("mod.delete_workspace", "Delete workspace folder.", "packageName", "confirm"),
                Tool("fs.list", "List files in a workspace.", "packageName", "path?"),
                Tool("fs.read", "Read a UTF-8 text file.", "packageName", "path"),
                Tool("fs.write", "Write a UTF-8 text file.", "packageName", "path", "content"),
                Tool("mod.compile", "dotnet build -c Release.", "packageName"),
                Tool("mod.pack", "Pack out/*.scmod.", "packageName"),
                Tool("mod.install", "Copy scmod into Mods/.", "packageName", "reload?"),
                Tool("mod.uninstall", "Delete installed scmod.", "packageName", "reload?"),
                Tool("game.reload", "Dispose mods and return to Loading.", "confirm"),
                Tool("game.log", "Tail Game.log and studio buffer.", "tail?")
            ];
            return StudioResult.Success(new JsonObject { ["tools"] = tools });
        }

        static JsonObject Tool(string name, string description, params string[] args) {
            JsonArray array = [];
            foreach (string arg in args) {
                array.Add(arg);
            }
            return new JsonObject {
                ["name"] = name,
                ["description"] = description,
                ["arguments"] = array
            };
        }

        static StudioResult ListMods() {
            JsonArray projects = [];
            foreach (StudioProjectInfo info in StudioWorkspace.ListProjects()) {
                projects.Add(ProjectJson(info, StudioModLifecycle.IsInstalled(info.PackageName)));
            }
            return StudioResult.Success(new JsonObject { ["projects"] = projects });
        }

        static StudioResult Create(JsonObject args) {
            string displayName = StudioJson.GetString(args, "displayName");
            string packageName = StudioJson.GetString(args, "packageName");
            StudioProjectInfo info = BlankModTemplate.Create(displayName, packageName);
            return StudioResult.Success(ProjectJson(info, false));
        }

        static StudioResult Get(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioProjectInfo info = StudioWorkspace.Require(packageName);
            return StudioResult.Success(ProjectJson(info, StudioModLifecycle.IsInstalled(packageName)));
        }

        static StudioResult DeleteWorkspace(JsonObject args) {
            string packageName = RequirePackage(args);
            if (!StudioJson.GetBool(args, "confirm")) {
                return StudioResult.Fail("invalid_argument", "confirm=true is required to delete a workspace.");
            }
            if (PackageNameRules.IsProtected(packageName)) {
                return StudioResult.Fail("forbidden", "Protected package.");
            }
            if (!StudioWorkspace.Exists(packageName)) {
                return StudioResult.Fail("not_found", "Workspace not found.");
            }
            StudioWorkspace.DeleteProject(packageName);
            return StudioResult.Success();
        }

        static StudioResult ListFiles(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioWorkspace.Require(packageName);
            string rel = StudioJson.GetString(args, "path", "") ?? "";
            if (!StudioPathGuard.TryResolve(StudioWorkspace.GetProjectSystemPath(packageName), rel, out string full, out string code)) {
                return StudioResult.Fail(code, "Path escapes the workspace.");
            }
            if (!Directory.Exists(full)) {
                return StudioResult.Fail("not_found", "Directory not found.");
            }
            JsonArray entries = [];
            foreach (string dir in Directory.GetDirectories(full)) {
                entries.Add(new JsonObject { ["name"] = Path.GetFileName(dir), ["isDir"] = true });
            }
            foreach (string file in Directory.GetFiles(full)) {
                entries.Add(new JsonObject { ["name"] = Path.GetFileName(file), ["isDir"] = false });
            }
            return StudioResult.Success(new JsonObject { ["path"] = rel, ["entries"] = entries });
        }

        static StudioResult ReadFile(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioWorkspace.Require(packageName);
            string rel = StudioJson.GetString(args, "path");
            if (string.IsNullOrEmpty(rel)) {
                return StudioResult.Fail("invalid_argument", "path is required.");
            }
            if (!StudioPathGuard.TryResolve(StudioWorkspace.GetProjectSystemPath(packageName), rel, out string full, out string code)) {
                return StudioResult.Fail(code, "Path escapes the workspace.");
            }
            if (!File.Exists(full)) {
                return StudioResult.Fail("not_found", "File not found.");
            }
            string ext = Path.GetExtension(full);
            if (!s_textExtensions.Contains(ext)) {
                return StudioResult.Fail("not_supported", "Only text files can be read in M1.");
            }
            FileInfo fileInfo = new(full);
            if (fileInfo.Length > 512 * 1024) {
                return StudioResult.Fail("not_supported", "File exceeds 512KiB.");
            }
            string content = File.ReadAllText(full);
            return StudioResult.Success(new JsonObject { ["path"] = rel.Replace('\\', '/'), ["content"] = content });
        }

        static StudioResult WriteFile(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioWorkspace.Require(packageName);
            string rel = StudioJson.GetString(args, "path");
            if (string.IsNullOrEmpty(rel)) {
                return StudioResult.Fail("invalid_argument", "path is required.");
            }
            if (!StudioPathGuard.TryResolve(StudioWorkspace.GetProjectSystemPath(packageName), rel, out string full, out string code)) {
                return StudioResult.Fail(code, "Path escapes the workspace.");
            }
            string ext = Path.GetExtension(full);
            if (!s_textExtensions.Contains(ext) && !string.Equals(Path.GetFileName(full), ".scmodstudio.json", StringComparison.OrdinalIgnoreCase)) {
                return StudioResult.Fail("not_supported", "Only text files can be written in M1.");
            }
            string content = StudioJson.GetString(args, "content") ?? "";
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(full, content);
            return StudioResult.Success(new JsonObject { ["path"] = rel.Replace('\\', '/'), ["bytes"] = content.Length });
        }

        static StudioResult Compile(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioProjectInfo info = StudioWorkspace.Require(packageName);
            string csproj = StudioWorkspace.FindCsproj(packageName);
            CompileOutcome outcome = DotnetCompiler.Build(csproj);
            info.LastCompileUtc = DateTime.UtcNow.ToString("o");
            info.LastCompileOk = outcome.Ok;
            StudioWorkspace.Save(info);
            StudioLogReader.Append(outcome.Log);
            JsonObject data = new() {
                ["ok"] = outcome.Ok,
                ["log"] = outcome.Log ?? "",
                ["elapsedMs"] = outcome.ElapsedMs,
                ["exitCode"] = outcome.ExitCode
            };
            if (outcome.Ok) {
                return StudioResult.Success(data);
            }
            StudioResult fail = StudioResult.Fail(outcome.Code ?? "compile_failed", outcome.Ok ? "compile failed" : "Compile failed.");
            fail.Data = data;
            return fail;
        }

        static StudioResult Pack(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioProjectInfo info = StudioWorkspace.Require(packageName);
            string output = StudioWorkspace.OutputDirectory(info);
            string dest = StudioWorkspace.PackOutputPath(info);
            ModPacker.PackOutputDirectory(output, info.AssemblyName, dest);
            info.LastPackUtc = DateTime.UtcNow.ToString("o");
            StudioWorkspace.Save(info);
            FileInfo file = new(dest);
            return StudioResult.Success(new JsonObject {
                ["scmodPath"] = dest.Replace('\\', '/'),
                ["size"] = file.Exists ? file.Length : 0
            });
        }

        static StudioResult Install(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioProjectInfo info = StudioWorkspace.Require(packageName);
            string dest = StudioWorkspace.PackOutputPath(info);
            if (!File.Exists(dest)) {
                Pack(args);
            }
            string imported = StudioModLifecycle.Install(info);
            info.LastInstallUtc = DateTime.UtcNow.ToString("o");
            StudioWorkspace.Save(info);
            if (StudioJson.GetBool(args, "reload")) {
                StudioModLifecycle.RequestReload();
            }
            return StudioResult.Success(new JsonObject { ["importedName"] = imported });
        }

        static StudioResult Uninstall(JsonObject args) {
            string packageName = RequirePackage(args);
            StudioProjectInfo info = StudioWorkspace.Exists(packageName) ? StudioWorkspace.Require(packageName) : new StudioProjectInfo {
                PackageName = packageName,
                AssemblyName = PackageNameRules.ToAssemblyName(packageName)
            };
            StudioModLifecycle.Uninstall(packageName, info.AssemblyName);
            if (StudioJson.GetBool(args, "reload")) {
                StudioModLifecycle.RequestReload();
            }
            return StudioResult.Success();
        }

        static StudioResult Reload(JsonObject args) {
            if (!StudioJson.GetBool(args, "confirm")) {
                return StudioResult.Fail("invalid_argument", "confirm=true is required to reload the game.");
            }
            StudioModLifecycle.RequestReload();
            return StudioResult.Success(new JsonObject { ["scheduled"] = true });
        }

        static StudioResult GameLog(JsonObject args) {
            int tail = StudioJson.GetInt(args, "tail", 200);
            return StudioResult.Success(new JsonObject { ["text"] = StudioLogReader.Tail(tail) });
        }

        static string RequirePackage(JsonObject args) {
            string packageName = StudioJson.GetString(args, "packageName");
            if (string.IsNullOrWhiteSpace(packageName)) {
                throw new ArgumentException("packageName is required.");
            }
            return packageName.Trim();
        }

        static JsonObject ProjectJson(StudioProjectInfo info, bool installed) {
            JsonObject obj = info.ToJson();
            obj["installed"] = installed;
            obj["workspacePath"] = StudioWorkspace.GetProjectSystemPath(info.PackageName).Replace('\\', '/');
            return obj;
        }
    }
}
