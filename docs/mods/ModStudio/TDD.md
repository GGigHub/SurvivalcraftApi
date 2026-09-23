# ModStudio 技术设计文档（TDD）

| 字段 | 内容 |
|------|------|
| 文档版本 | 1.0.0 |
| 关联需求 | [PRD.md](PRD.md) |
| 关联技术方案 | [Solution.md](Solution.md) |
| 宿主参考 | Survivalcraft API 1.9.x（`APIVersionString = 1.9.3.1`） |
| 实现位置 | [`mods/ModStudio/`](../../../mods/ModStudio/) |

类名、路径、工具名、错误码以本文为准。需求与验收以 PRD 为准。

---

## 1. 工程结构

```
mods/ModStudio/
  ModStudio.csproj
  modinfo.json
  README.md
  Mod/ModStudioLoader.cs
  Screen/ModStudioScreen.cs
  Core/PackageNameRules.cs
  Core/StudioPathGuard.cs
  Core/StudioProjectInfo.cs
  Core/StudioError.cs
  Core/StudioJson.cs
  Workspace/StudioWorkspace.cs
  Workspace/BlankModTemplate.cs
  Compile/DotnetCompiler.cs
  Pack/ModPacker.cs
  Install/StudioModLifecycle.cs
  Agent/StudioSettings.cs
  Agent/StudioToolService.cs
  Agent/StudioHttpRouter.cs
  Agent/StudioHttpServer.cs
  Agent/StudioLogReader.cs
  Assets/Lang/zh-CN.json
  Assets/Lang/en-US.json
mods/ModStudio.Tests/
  PackageNameRulesTests.cs
  StudioPathGuardTests.cs
  StudioHttpRouterTests.cs
  ModPackerTests.cs
```

目标框架 `net10.0`，`Nullable` disable，引用宿主 `Survivalcraft` 工程（`Private=false`）。  
构建后 `PackageMod` Target 打出 `ModStudio.scmod` 并尽量复制到 `Survivalcraft.Windows/bin/Debug/Mods/`。

---

## 2. 宿主集成

### 2.1 ModLoader

```csharp
public class ModStudioLoader : ModLoader {
    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnMainMenuScreenCreated", this);
    }

    public override void OnLoadingFinished(List<Action> actions) {
        actions.Add(() => {
            if (ScreensManager.FindScreen<Screen>("ModStudio") == null) {
                ScreensManager.AddScreen("ModStudio", new ModStudioScreen());
            }
            StudioHttpServer.Instance.Start();
        });
    }

    public override void OnMainMenuScreenCreated(...) {
        rightBottomBar.Children.Add(new ModStudioMenuEntryWidget());
    }

    public override void ModDispose() {
        StudioHttpServer.Instance.Stop();
    }
}
```

屏幕名：`"ModStudio"`。返回：`ScreensManager.SwitchScreen("MainMenu")`。

### 2.2 重载

`StudioModLifecycle.RequestReload()`：

1. `SettingsManager.SaveSettings()`
2. 遍历 `ModsManager.ModList.ToArray()` 调用 `Dispose()`
3. `ScreensManager.SwitchScreen("Loading")`

若当前不在主线程，先 `Dispatcher.Dispatch(..., waitUntilCompleted: false)`（重载会拆掉当前 Screen，禁止在 HTTP 工作线程同步等待重载完成）。

---

## 3. 数据模型

### 3.1 `StudioProjectInfo`（`.scmodstudio.json`）

| 字段 | 类型 | 说明 |
|------|------|------|
| `SchemaVersion` | int | 当前为 `1` |
| `PackageName` | string | 工作区目录名 |
| `DisplayName` | string | 显示名 |
| `AssemblyName` | string | dll / scmod 主名 |
| `CreatedUtc` | string | ISO-8601 |
| `LastCompileUtc` | string | 可空 |
| `LastCompileOk` | bool | |
| `LastPackUtc` | string | 可空 |
| `LastInstallUtc` | string | 可空 |

### 3.2 `StudioSettings`（`{DocPath}/ModStudio.settings.json`）

| 字段 | 默认 |
|------|------|
| `ListenAddress` | `127.0.0.1`（写了别的也强制回环） |
| `Port` | `17865` |
| `Token` | 首次生成 `Guid`（N 格式） |
| `AgentEnabled` | `true` |

### 3.3 包名规则（`PackageNameRules`）

- 匹配：`^[a-z][a-z0-9]*(\.[a-z0-9]+)+$`
- 长度 3～80
- 黑名单：`survivalcraft`、`fastdebug`、`com.scspm.modstudio`
- 显示名 → 程序集：取字母数字，首字母大写段拼接；若为空则 `StudioMod`
- 显示名 → 建议包名：`com.studio.{slug}`，slug 为小写字母数字

---

## 4. 工作区与模板

### 4.1 根路径

```csharp
Storage.CombinePaths(ModsManager.DocPath, "ModsStudio")
```

系统路径用 `Storage.GetSystemPath` 交给 `dotnet` 与 `ZipFile`。

### 4.2 空白模板文件

| 路径 | 内容要点 |
|------|----------|
| `{Assembly}.csproj` | net10.0，HintPath = `typeof(ModsManager).Assembly` 目录下三件套 dll |
| `modinfo.json` | Name/PackageName/ApiVersion/`NonPersistentMod=true`/`Cosmetic` |
| `Mod/{Assembly}Loader.cs` | 注册 `OnLoadingFinished`，打一条 Information |
| `Assets/Lang/zh-CN.json` | 占位 |
| `Assets/Lang/en-US.json` | 占位 |
| `AGENTS.md` | 禁止改宿主、优先 Hook、异常捕获 |
| `README.md` | 如何用工坊编译安装 |
| `.scmodstudio.json` | 清单 |

游戏目录解析：

```csharp
string gameDir = Path.GetDirectoryName(typeof(ModsManager).Assembly.Location);
```

---

## 5. 编译与打包

### 5.1 `DotnetCompiler`

- 命令：`dotnet`，参数：`build "{csproj}" -c Release --nologo -v:m`
- 超时：180s
- 工作目录：项目根（系统路径）
- 找不到可执行文件 → `sdk_missing`
- ExitCode ≠ 0 → `compile_failed`，`data.log` 为完整输出
- 成功后更新清单 `LastCompileUtc/Ok`

进程启动使用 `ProcessStartInfo`：`UseShellExecute=false`，重定向 stdout/stderr，不创建窗口。

### 5.2 `ModPacker`

输入：编译输出目录（`bin/Release/`，因 `AppendTargetFrameworkToOutputPath=false`）。  
收入 zip：

- `{AssemblyName}.dll`（必需）
- `modinfo.json`（必需）
- `Assets/**`（若有）
- `icon.png` / `icon.webp`（若有）

排除：`*.pdb`、`*.deps.json`、宿主 dll。  
使用 `System.IO.Compression.ZipArchive`，条目名正斜杠。  
输出：`{workspace}/out/{AssemblyName}.scmod`。

宿主用 `Game.ZipArchive.Open` 能读标准 deflate zip（现网 scmod 即此）。

---

## 6. 安装 / 卸载

`StudioModLifecycle`：

**Install**

1. 若无 `out/*.scmod` 则先 Pack。
2. 在 `ModListAll` 找同 `PackageName`：若为保护包 → `forbidden`；若存在则 `Storage.DeleteFile(ModFilePath)`。
3. 打开 scmod 流，`ModsManager.ImportMod(assemblyName, stream)`。
4. 更新清单。

**Uninstall**

1. 保护包 → `forbidden`。
2. 按 PackageName 找 `ModFilePath`，删除文件；若未加载则尝试删 `Mods/{Assembly}.scmod`。

**IsProtectedPackage**：黑名单 + 当前 ModStudio 实体。

---

## 7. 工具契约（界面与 HTTP 共用）

`StudioToolService.Call(name, argumentsJson) → StudioToolResult`

| 工具名 | 参数 | 成功 data |
|--------|------|-----------|
| `tools.catalog` | — | 工具列表（name/description/args） |
| `mod.list` | — | `projects[]` |
| `mod.create` | `displayName`，可选 `packageName` | 项目信息 |
| `mod.get` | `packageName` | 项目信息 + 是否已安装 |
| `mod.delete_workspace` | `packageName`，`confirm=true` | ok |
| `fs.list` | `packageName`，`path` 默认 `""` | `entries[]` {name,isDir} |
| `fs.read` | `packageName`，`path` | `{ path, content }` 文本 UTF-8 |
| `fs.write` | `packageName`，`path`，`content` | `{ path, bytes }` |
| `mod.compile` | `packageName` | `{ ok, log, elapsedMs }` |
| `mod.pack` | `packageName` | `{ scmodPath, size }` |
| `mod.install` | `packageName`，可选 `reload` | `{ importedName }` |
| `mod.uninstall` | `packageName`，可选 `reload` | ok |
| `game.reload` | `confirm=true` | `{ scheduled: true }` |
| `game.log` | 可选 `tail` 默认 200 | `{ text }` |

二进制资源 `fs.read` 超过 512KiB 或非文本扩展名 → `not_supported`（M1 只保证文本：`.cs .json .md .txt .csproj .xml .csv .cr .xdb .clo`）。

---

## 8. HTTP

### 8.1 路由（`StudioHttpRouter`）

| 方法 | 路径 | 工具 |
|------|------|------|
| GET | `/health` | 无鉴权，`{ ok, version, agent }` |
| GET | `/v1/tools` | `tools.catalog` |
| POST | `/v1/tools/call` | body `{ name, arguments }` |
| GET | `/v1/mods` | `mod.list` |
| POST | `/v1/mods` | `mod.create` |
| GET | `/v1/mods/{pkg}` | `mod.get` |
| DELETE | `/v1/mods/{pkg}` | `mod.delete_workspace`（query confirm） |
| GET | `/v1/mods/{pkg}/files` | `fs.list` |
| GET | `/v1/mods/{pkg}/file` | `fs.read` query path |
| PUT | `/v1/mods/{pkg}/file` | `fs.write` |
| POST | `/v1/mods/{pkg}/compile` | `mod.compile` |
| POST | `/v1/mods/{pkg}/pack` | `mod.pack` |
| POST | `/v1/mods/{pkg}/install` | `mod.install` |
| POST | `/v1/mods/{pkg}/uninstall` | `mod.uninstall` |
| POST | `/v1/game/reload` | `game.reload` |
| GET | `/v1/game/log` | `game.log` |

包名可含点，路由用前缀匹配，不用单段 path。

### 8.2 鉴权与响应

- Header：`Authorization: Bearer {token}` 或 `X-Studio-Token`
- 401：`{ ok:false, error:{ code:"unauthorized", message } }`
- 业务错误 HTTP 仍可用 200 + `ok:false`（降低 Agent 重试复杂度），非法路由 404
- CORS：不需要（非浏览器产品面）

### 8.3 `game.log`

优先读 `ModsManager.LogPath/Game.log` 尾部；再拼工坊内存环形缓冲（最近 200 条工具/编译日志）。

---

## 9. 界面（`ModStudioScreen`）

代码构建 Widget 树（不新增宿主 XML）：

```
顶栏：[返回] 标题 [Agent状态]
中部左：项目 ListPanel
中部右：详情（包名/程序集/编译状态）+ 按钮行
        [新建] [删除工作区] [编译] [打包] [安装] [卸载] [重载]
底部：日志 Label（尾部约 4000 字）
底栏：http://127.0.0.1:17865  Token（可点击复制到剪贴板，若 API 不可用则对话框展示）
```

新建：`TextBoxDialog` 输入显示名。  
危险操作：`MessageDialog` 确认。  
编译：后台 `Task`，`CancellableBusyDialog`，完成 `Dispatcher` 刷新。

剪贴板：优先 `Engine` / `TextCopy` 若宿主已引用；否则对话框展示全文。

---

## 10. 国际化键（`ModStudio`）

`MenuButton` `Title` `Back` `New` `Delete` `Compile` `Pack` `Install` `Uninstall` `Reload` `AgentOn` `AgentOff` `Token` `ConfirmReload` `SdkMissing` `Protected` `CreateTitle` `EmptyList` `LogEmpty` …

实现以 `LanguageControl.Get("ModStudio", key)` + 英文/中文 Fallback。

---

## 11. 测试

| 测试 | 断言 |
|------|------|
| `PackageNameRulesTests` | 合法/非法/黑名单/建议包名 |
| `StudioPathGuardTests` | `..`、绝对路径、正常相对路径 |
| `StudioHttpRouterTests` | 上表路由解析；未知路径 null |
| `ModPackerTests` | 临时目录打 zip，含 dll 与 Assets，不含 cs |

游戏内按 PRD UC-01～UC-05、UC-10～UC-13 手工验收。

---

## 12. 构建与本地测试

```bat
dotnet test mods/ModStudio.Tests/ModStudio.Tests.csproj
dotnet build mods/ModStudio/ModStudio.csproj
```

1. 确认 `Survivalcraft.Windows/bin/Debug/Mods/ModStudio.scmod` 存在。
2. 启动游戏，主菜单右下角「模组工坊」。
3. 走 UC-01。
4. 用 curl / 浏览器插件带 Token 打 `/health` 与 `/v1/mods`。

发布：只分发 `ModStudio.scmod`。
