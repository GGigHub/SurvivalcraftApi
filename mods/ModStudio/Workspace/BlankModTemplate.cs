using Engine;

namespace Game {
    public static class BlankModTemplate {
        public static StudioProjectInfo Create(string displayName, string packageName) {
            if (string.IsNullOrWhiteSpace(displayName)) {
                throw new ArgumentException("Display name is empty.");
            }
            string name = displayName.Trim();
            string pkg = string.IsNullOrWhiteSpace(packageName) ? PackageNameRules.SuggestPackageName(name) : packageName.Trim();
            if (!PackageNameRules.TryValidate(pkg, out string error)) {
                throw new InvalidOperationException(error);
            }
            if (StudioWorkspace.Exists(pkg)) {
                throw new InvalidOperationException("Workspace already exists.");
            }
            string assembly = PackageNameRules.ToAssemblyName(name);
            string dir = StudioWorkspace.GetProjectSystemPath(pkg);
            Directory.CreateDirectory(Path.Combine(dir, "Mod"));
            Directory.CreateDirectory(Path.Combine(dir, "Assets", "Lang"));
            string gameDir = Path.GetDirectoryName(typeof(ModsManager).Assembly.Location) ?? "";
            File.WriteAllText(Path.Combine(dir, assembly + ".csproj"), Csproj(assembly, gameDir));
            File.WriteAllText(Path.Combine(dir, "modinfo.json"), ModInfo(name, pkg));
            File.WriteAllText(Path.Combine(dir, "Mod", assembly + "Loader.cs"), Loader(assembly, name));
            File.WriteAllText(Path.Combine(dir, "Assets", "Lang", "en-US.json"), LangJson(assembly, "Created by ModStudio."));
            File.WriteAllText(Path.Combine(dir, "Assets", "Lang", "zh-CN.json"), LangJson(assembly, "由模组工坊创建。"));
            File.WriteAllText(Path.Combine(dir, "AGENTS.md"), AgentsMd());
            File.WriteAllText(Path.Combine(dir, "README.md"), Readme(name, pkg, assembly));
            StudioProjectInfo info = new() {
                SchemaVersion = 1,
                PackageName = pkg,
                DisplayName = name,
                AssemblyName = assembly,
                CreatedUtc = DateTime.UtcNow.ToString("o")
            };
            StudioWorkspace.Save(info);
            return info;
        }

        static string Csproj(string assembly, string gameDir) {
            string engine = Path.Combine(gameDir, "Engine.dll").Replace('\\', '/');
            string entity = Path.Combine(gameDir, "EntitySystem.dll").Replace('\\', '/');
            string survival = Path.Combine(gameDir, "Survivalcraft.dll").Replace('\\', '/');
            return string.Join(
                "\n",
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <TargetFramework>net10.0</TargetFramework>",
                "    <ImplicitUsings>enable</ImplicitUsings>",
                "    <Nullable>disable</Nullable>",
                "    <LangVersion>preview</LangVersion>",
                "    <RootNamespace>Game</RootNamespace>",
                $"    <AssemblyName>{assembly}</AssemblyName>",
                "    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>",
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <Reference Include=\"Engine\">",
                $"      <HintPath>{engine}</HintPath>",
                "      <Private>false</Private>",
                "    </Reference>",
                "    <Reference Include=\"EntitySystem\">",
                $"      <HintPath>{entity}</HintPath>",
                "      <Private>false</Private>",
                "    </Reference>",
                "    <Reference Include=\"Survivalcraft\">",
                $"      <HintPath>{survival}</HintPath>",
                "      <Private>false</Private>",
                "    </Reference>",
                "  </ItemGroup>",
                "  <ItemGroup>",
                "    <None Include=\"modinfo.json\" CopyToOutputDirectory=\"PreserveNewest\" />",
                "    <None Include=\"Assets\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />",
                "  </ItemGroup>",
                "</Project>",
                ""
            );
        }

        static string ModInfo(string name, string packageName) =>
            "{\n" +
            $"  \"Name\": {Quote(name)},\n" +
            "  \"Version\": \"1.0.0\",\n" +
            $"  \"ApiVersion\": {Quote(ModsManager.APIVersionString)},\n" +
            $"  \"Description\": {Quote(name + " — created with ModStudio.")},\n" +
            $"  \"ScVersion\": {Quote(ModsManager.GameVersion)},\n" +
            $"  \"PackageName\": {Quote(packageName)},\n" +
            "  \"Author\": \"ModStudio\",\n" +
            "  \"NonPersistentMod\": true,\n" +
            "  \"GameplayImpactLevel\": \"Cosmetic\",\n" +
            "  \"LoadOrder\": 50000\n" +
            "}\n";

        static string Loader(string assembly, string displayName) =>
            "using Engine;\n\n" +
            "namespace Game {\n" +
            $"    public class {assembly}Loader : ModLoader {{\n" +
            "        public override void __ModInitialize() {\n" +
            "            ModsManager.RegisterHook(\"OnLoadingFinished\", this);\n" +
            "        }\n\n" +
            "        public override void OnLoadingFinished(List<Action> actions) {\n" +
            "            actions.Add(() => {\n" +
            $"                Log.Information(\"[Studio] {EscapeCs(displayName)} loaded.\");\n" +
            "            });\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        static string LangJson(string assembly, string hello) =>
            "{\n" +
            $"  {Quote(assembly)}: {{\n" +
            $"    \"Hello\": {Quote(hello)}\n" +
            "  }\n" +
            "}\n";

        static string AgentsMd() =>
            """
# Agent 约束（本工作区）

你正在为生存战争插件版（Survivalcraft API）开发一个模组，而不是改游戏本体。

## 必须

- 只改本工作区文件。禁止修改游戏安装目录里的 `Survivalcraft/`、`Engine/` 源码或已安装的其它 `.scmod`。
- 优先使用 `ModLoader` 钩子；需要注入时先向作者确认，不要默认 `Harmony.PatchAll()`。
- 模组异常必须捕获并 `Log.Error`，不能让游戏崩溃。
- 保持 `PackageName` 稳定；`ApiVersion` 与当前游戏 API 对齐。
- 新功能优先子系统 / 组件 / Screen，避免覆盖原版类。

## 建议

- 不进存档的功能设 `NonPersistentMod: true`。
- 提供 zh-CN 与 en-US。
- 编译失败时根据 `mod.compile` 返回的日志修改，不要猜测宿主内部 API。

## 参考

- 宿主文档：`docs/Architecture.md`、`docs/HowToDevelopAMod.md`、`docs/ModInfoConfig.md`
""";

        static string Readme(string name, string packageName, string assembly) =>
            $"# {name}\n\n" +
            $"PackageName: `{packageName}`  \n" +
            $"Assembly: `{assembly}`\n\n" +
            "在游戏「模组工坊」中编译、打包、安装。也可以通过本机 Agent API 调用 `mod.compile` / `mod.install`。\n";

        static string Quote(string text) => "\"" + EscapeJson(text) + "\"";

        static string EscapeJson(string text) =>
            (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        static string EscapeCs(string text) =>
            (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
