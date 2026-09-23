# ModStudio 产品需求文档（PRD）

| 字段 | 内容 |
|------|------|
| 产品名称 | ModStudio（模组工坊 / 模组创作模式） |
| 文档版本 | 1.0.0 |
| 状态 | 已定稿（M0 / M1 可开工） |
| 宿主 | Survivalcraft API（插件版） |
| 发布形态 | `.scmod` 工具模组（不发布、不修改游戏本体） |
| 关联技术方案 | [Solution.md](Solution.md) |
| 关联详细设计 | [TDD.md](TDD.md) |
| 保密级别 | 内部 / 社区开源（无付费墙） |

---

## 1. 背景与定位

### 1.1 问题陈述

插件版已经具备成熟的模组运行时（`.scmod` 加载、Hook、资源合并、社区内容），但**创作闭环仍停在 IDE 门外**：

- 作者必须自行安装 SDK、克隆示例仓库、手写 `csproj`、手动拷贝 `.scmod`、重启游戏验收。
- 外部 AI Agent（Cursor、Claude Code、OpenClaw）只能「读文档写代码」，无法编译、安装、看日志、卸载重来。
- 玩家有想法但过不了工程门槛，社区产出速度受限于少数熟悉 MSBuild 的作者。

现有 `FastDebugModEntity` 只服务「已放进 `Mods/` 的松散文件」，**不编译 C#、不管理项目、不对外提供工具面**。

### 1.2 产品定位

ModStudio 是安装在游戏内的 **Mod 创作工作台 + Agent 工具运行时**：

> 玩家或作者在主菜单进入「模组工坊」，用模板创建项目；一键编译、打包、安装、卸载、重载。  
> 同一套能力通过本机 HTTP 暴露给外部 AI Agent，使 Agent 可以协助或主力开发模组。

它**不是**游戏本体功能、**不是**在线 IDE、**不是**完整 CI、**不是**社区商店后台。

### 1.3 价值主张

| 角色 | 一句话 |
|------|--------|
| 模组作者 | 少离开游戏就能完成「改代码 → 看见结果」 |
| AI Agent | 获得受约束的文件系统与生命周期工具，而不是裸盘权限 |
| 发行 / 社区 | 降低新模组供给成本，且不污染宿主公开 API |
| 玩家（轻度创作者） | 用模板做出可安装的第一个模组 |

### 1.4 成功指标（M0 + M1）

| 指标 | 达标标准 | 度量方式 |
|------|----------|----------|
| 可达性 | 安装 ModStudio 后主菜单可见「模组工坊」 | 手工验收 |
| 空项目闭环 | 从模板新建 → 编译成功 → 打出 `.scmod` → 安装 → 重载后模组出现在模组列表 | 验收用例 UC-01 |
| 卸载闭环 | 卸载刚安装的作品模组后重载，列表中消失，存档无「缺少模组」警告（作品为 NonPersistent） | UC-02 |
| 失败可见 | 故意写坏 C# 后编译失败，界面与 API 返回编译器原文 | UC-03 |
| Agent 可达 | `GET /health` 在 127.0.0.1 返回 ok；无 Token 被拒绝 | UC-10 |
| Agent 闭环 | 外部用 Token 调用 `mod.create` → `fs.write` → `mod.compile` → `mod.install` | UC-11 |
| 安全底线 | API 不监听非回环地址；路径无法逃出工作区；不能卸载/覆盖 ModStudio 自身 | UC-20 |
| 发布就绪 | 产出可安装的 `ModStudio.scmod`，不打包游戏本体 | 构建产物 |

本期不设 DAU / 付费指标。后续若做 M3 对话，再单独立北极星（「从想法到可玩模组的中位时长」）。

---

## 2. 用户与场景

### 2.1 人物

| 角色 | 描述 | 核心任务 |
|------|------|----------|
| **独立作者** | 会一点 C#，已会用 Visual Studio / Rider | 加快编译安装循环，少切窗口 |
| **Agent 主程** | 人只描述需求，Cursor / Claude Code 写代码 | 让 Agent 具备 compile / install / log |
| **轻度创作者** | 几乎不会工程，想改个按钮或加个方块 | 模板起步，看日志找人帮忙 |
| **审核 / 发行** | 关心安全与兼容 | 工具模组不进存档、不改本体 |

### 2.2 关键用户旅程

**J1 手工闭环（M0）**

1. 启动游戏，主菜单点「模组工坊」。
2. 点「新建」，输入显示名（如 `HelloWorld`）。
3. 工坊生成工作区、模板源码、`modinfo.json`、`AGENTS.md`。
4. 点「编译」，成功后点「打包」「安装」。
5. 确认重载，回到主菜单，模组列表可见新模组。
6. 可再卸载并重载，作品模组消失。

**J2 Agent 主力（M1）**

1. 玩家在工坊看到本机地址与 Token（可复制）。
2. 外部 Agent 配置 Base URL + Bearer Token。
3. Agent 调 `tools.catalog` 发现工具，再 `mod.create` / `fs.write` / `mod.compile`。
4. 编译失败则读返回的 diagnostics，改文件再编。
5. `mod.install` 后提示玩家在游戏内确认重载（默认不自动重载）。

### 2.3 非用户

- 浏览器 / iOS 玩家（宿主不支持模组程序集）。
- 需要在**已打开的生存存档里**热换方块表 / Harmony 的作者（M2 以后，且完整档仍须重载）。

---

## 3. 范围

### 3.1 In Scope（M0 + M1）

**M0 创作闭环**

- 主菜单入口与独立全屏 `ModStudioScreen`
- 工作区根目录位于游戏文档目录（`doc/ModsStudio/`），**不写入 `Mods/` 源码**
- 从空白模板新建项目（csproj、ModLoader 骨架、中英语言、AGENTS.md）
- 列出 / 打开 / 删除工作区
- 调用本机 `dotnet build` 编译
- 将输出打成标准 `.scmod`（zip：dll + `modinfo.json` + `Assets/`）
- 安装到 `Mods/`（覆盖同包名旧包，禁止覆盖自身）
- 卸载已安装的作品模组（删 `.scmod`，禁止卸自身）
- 请求重载：公开 API 等价于宿主 `Reboot()`（Dispose + `SwitchScreen("Loading")`）
- 界面展示最近一次编译 / 打包 / 安装日志
- 中英文案

**M1 Agent 工具面**

- 仅监听 `127.0.0.1`
- Bearer Token（首次生成，持久化）
- 统一工具调度（界面与 HTTP 走同一 `StudioToolService`）
- REST + `POST /v1/tools/call` 两种入口
- 工具：`mod.list/create/get/delete_workspace`、`fs.list/read/write`、`mod.compile/pack/install/uninstall`、`game.log`、`game.reload`、`tools.catalog`
- `game.reload` 必须显式 `confirm=true`，默认关闭自动重载

### 3.2 Out of Scope（本期明确不做）

| 排除项 | 原因 |
|--------|------|
| 游戏内 LLM 对话框（M3） | 不阻塞工具面；先让外部 Agent 能干活 |
| 可卸载 ALC 预览热循环（M2） | 完整档仍无法热更；降低 M0/M1 风险 |
| 一键社区发布（M4） | 现网 Publish 是链接语义，需另接上传 |
| 改 `Survivalcraft/` 宿主代码 | 产品以模组交付，避免污染公开 API |
| 在线多人协作 / 云端仓库托管 | 超出单机工具 |
| 完整 IDE（语法高亮、调试器附加） | 用外部编辑器 / Agent |
| Browser / iOS | 宿主限制 |
| 自动执行 Harmony `PatchAll` 的默认模板 | 安全默认拒绝 |
| 监听 0.0.0.0 或局域网 | 任意代码执行面过大 |
| 把源码工作区放进 `Mods/` | 会与 `FastDebugModEntity` 冲突 |

### 3.3 模组元数据约定

| 字段 | 值 |
|------|------|
| `Name` | `ModStudio` |
| `PackageName` | `com.scspm.modstudio` |
| `GameplayImpactLevel` | `Cosmetic` |
| `NonPersistentMod` | `true` |
| `LoadOrder` | `80000`（辅助类，靠后，尽量不挡玩法模组） |
| `ApiVersion` | `1.9.3.1` |

作品模板默认同样 `NonPersistentMod=true`、`Cosmetic`，避免卸载后污染存档。作者可事后改 `modinfo.json`。

---

## 4. 功能需求

### 4.1 工作区（FR-W）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-W01 | 工作区根路径为 `{DocPath}/ModsStudio/{packageName}/` | P0 |
| FR-W02 | 包名须符合反转域名：小写 `[a-z0-9._]`，至少两段，禁止 `survivalcraft` / `fastdebug` / `com.scspm.modstudio` | P0 |
| FR-W03 | 新建时写入 `.scmodstudio.json` 状态清单 | P0 |
| FR-W04 | 删除工作区只删文档目录，不自动卸载已安装 `.scmod`（须二次确认） | P0 |
| FR-W05 | 相对路径读写禁止 `..` 与绝对路径逃逸 | P0 |

### 4.2 模板与工程（FR-T）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-T01 | 空白模板含：csproj（HintPath 指向当前游戏 dll）、`modinfo.json`、`ModLoader`、中英 Lang、`AGENTS.md`、`README.md` | P0 |
| FR-T02 | csproj 引用 `Engine` / `EntitySystem` / `Survivalcraft`，`Private=false` | P0 |
| FR-T03 | 模板 ModLoader 只注册 `OnLoadingFinished`，打一条 Information 日志，便于验收「装上了」 | P0 |
| FR-T04 | `AGENTS.md` 写明：禁止改宿主、优先 Hook、慎用 Harmony、异常必须捕获 | P0 |

### 4.3 编译打包（FR-C）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-C01 | 使用本机 `dotnet build -c Release`，工作目录为项目根 | P0 |
| FR-C02 | 找不到 SDK 或 `dotnet` 不在 PATH 时，返回可行动错误（提示安装 .NET 10） | P0 |
| FR-C03 | 编译输出（stdout/stderr）完整保留，界面可滚动查看 | P0 |
| FR-C04 | 打包内容：`{AssemblyName}.dll`、`modinfo.json`、`Assets/**`；排除 `bin/obj` 与源码 | P0 |
| FR-C05 | 产物写到工作区 `out/{AssemblyName}.scmod` | P0 |

### 4.4 安装卸载重载（FR-L）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-L01 | 安装调用公开 `ModsManager.ImportMod`；目标已存在则先删除再导入 | P0 |
| FR-L02 | 禁止安装/卸载/覆盖 `com.scspm.modstudio`、`survivalcraft`、`fastdebug` | P0 |
| FR-L03 | 卸载按 PackageName 定位 `ModEntity.ModFilePath` 并删除文件 | P0 |
| FR-L04 | 重载：保存设置、Dispose 已启用模组、`SwitchScreen("Loading")` | P0 |
| FR-L05 | 界面在安装/卸载后询问是否立即重载，默认由玩家确认 | P0 |
| FR-L06 | HTTP `mod.install` / `mod.uninstall` 默认 `reload=false` | P0 |

### 4.5 Agent API（FR-A，M1）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-A01 | 默认 `http://127.0.0.1:17865/`，可在设置文件改端口 | P0 |
| FR-A02 | 除 `/health` 外均需 `Authorization: Bearer <token>` | P0 |
| FR-A03 | Token 存 `{DocPath}/ModStudio.settings.json`，不进世界存档 | P0 |
| FR-A04 | `GET /v1/tools` 与 `POST /v1/tools/call` 提供机器可读工具目录 | P0 |
| FR-A05 | 所有工具错误返回稳定 JSON：`{ ok, error.code, error.message }` | P0 |
| FR-A06 | 服务在 ModLoader 初始化后启动，`ModDispose` 时停止 | P0 |
| FR-A07 | 界面展示地址、端口、Token、运行状态；可复制 Token | P0 |

### 4.6 体验与国际化（FR-X）

| ID | 需求 | 优先级 |
|----|------|--------|
| FR-X01 | 任意时刻可返回主菜单，不打开存档 | P0 |
| FR-X02 | 长操作（编译）不卡死渲染线程，完成后回主线程刷新 UI | P0 |
| FR-X03 | zh-CN / en-US 文案齐全 | P0 |
| FR-X04 | 操作失败用对话框 + 日志区双通道提示 | P0 |

---

## 5. 非功能需求

| ID | 类别 | 要求 |
|----|------|------|
| NFR-01 | 安全 | 只绑定回环；Token 随机 128 bit 以上；工作区沙箱 |
| NFR-02 | 兼容 | 不修改宿主程序集；不依赖 `internal Reboot()` |
| NFR-03 | 性能 | 空闲时 CPU 可忽略；HTTP 并发以「单作者本机」为目标（≤ 4 请求） |
| NFR-04 | 体积 | ModStudio 自身不嵌入 Roslyn；`.scmod` 保持小型工具体量 |
| NFR-05 | 可靠性 | 编译/HTTP 异常一律捕获并记录，不让游戏崩溃 |
| NFR-06 | 可观测 | 每次工具调用写一条 Information 日志（不含完整 Token） |
| NFR-07 | 可测试 | 包名、路径沙箱、HTTP 路由、打包格式可脱离游戏单测 |
| NFR-08 | 平台 | 承诺 Windows；Linux 尽力（`dotnet` 在 PATH）；Android 编译器路径不承诺 |

---

## 6. 里程碑

### M0 — 工作台闭环

- 主菜单进出、新建模板、列表、编译、打包、安装、卸载、重载、中英、失败日志。
- 验收：UC-01～UC-05。

### M1 — Agent 工具 API

- 本机 HTTP、Token、工具目录、与 M0 同一套服务。
- 验收：UC-10～UC-13、UC-20。

### M2+（路标，非本期）

- M2：预览档 collectible ALC。
- M3：游戏内 OpenAI 兼容对话。
- M4：打包校验 + 社区上传（人工确认）。

---

## 7. 验收用例

### UC-01 新建并安装空白模组

1. 进入模组工坊，新建 `HelloStudio`。
2. 工作区出现 `modinfo.json` 与 `*.csproj`。
3. 编译成功（本机已装 .NET 10 SDK）。
4. 打包得到 `out/HelloStudio.scmod`。
5. 安装并确认重载。
6. **期望**：模组管理中可见 HelloStudio；日志含模板 ModLoader 的加载信息。

### UC-02 卸载作品模组

1. 在工坊对 HelloStudio 点卸载并重载。
2. **期望**：`.scmod` 文件删除；模组列表无该包名；既有世界可进且无「缺少该模组」警告。

### UC-03 编译失败可见

1. 把模板 `ModLoader` 改成语法错误。
2. 编译。
3. **期望**：失败状态；日志含 C# 错误行号；不产生或不覆盖成功 `.scmod`。

### UC-04 不能伤害自身

1. 尝试卸载或覆盖安装 `com.scspm.modstudio`。
2. **期望**：拒绝并提示。

### UC-05 返回主菜单

1. 在工坊点返回。
2. **期望**：回到主菜单，无需打开存档。

### UC-10 Agent 健康检查

1. 启动游戏进入主菜单（工坊已加载）。
2. 无 Token 访问 `/health`。
3. 无 Token 访问 `/v1/mods`。
4. **期望**：health=200；mods=401。

### UC-11 Agent 写文件并编译

1. 带 Token `mod.create`。
2. `fs.write` 修改 Loader 日志字符串。
3. `mod.compile` 成功。
4. **期望**：与界面编译等价。

### UC-12 路径逃逸

1. `fs.read` path=`../Settings.xml` 或 `C:/Windows/win.ini`。
2. **期望**：`error.code=path_escape`。

### UC-13 默认不自动重载

1. HTTP `mod.install` 不带 reload。
2. **期望**：文件已拷贝，游戏未自行进 Loading。

### UC-20 只绑回环

1. 用本机局域网 IP 访问同一端口。
2. **期望**：连接失败或拒绝（不得对 0.0.0.0 listen）。

---

## 8. 风险与依赖

| 风险 | 影响 | 缓解 |
|------|------|------|
| 玩家未装 .NET 10 SDK | 编译不可用 | 明确错误文案 + 官网链接；不假装内嵌编译器 |
| `HttpListener` 在部分 Windows 需 URL ACL | Agent 起不来 | 失败时界面标红，M0 仍可用；仅绑 127.0.0.1 |
| `SwitchScreen("Loading")` 与官方 Reboot 行为漂移 | 重载后状态脏 | 先 Dispose 再进 Loading；文档写明已知限制（dll 卸不掉） |
| Agent 写出恶意 Harmony | 进程内任意代码 | 模板不含 Patch；安装/重载需玩家确认；只监听本机 |
| 与 FastDebug 抢 `Mods/` | 资源错乱 | 工作区固定在 `doc/ModsStudio` |
| Tetris3D 等其它未提交工程 | 分支噪音 | 本分支只新增 ModStudio 文档与工程 |

**外部依赖**：.NET 10 SDK、游戏已能运行、桌面文件系统、回环网络。

---

## 9. 发布与商业约束

- 只分发 `ModStudio.scmod` 与本文档，不分发游戏本体或 API key。
- Token 属于本机密钥，不得写入社区帖或截图默认文案。
- 玩法影响为 Cosmetic，卸载本工具不影响存档。
- 不收集遥测。后续若加对话，API Key 由用户自备，存本地设置文件。

---

## 10. 开放问题（不阻塞 M0/M1）

1. 是否在宿主后续版本把 `Reboot()` 改为 `public`（体验一致，非必须）。
2. M2 预览档是否限制「禁止 Harmony / 禁止 Block」。
3. M3 对话供应商列表与默认模型。
4. 是否提供官方 MCP stdio 适配器（本期 HTTP 已够 Cursor 用）。
