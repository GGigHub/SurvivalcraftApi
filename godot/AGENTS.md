# Godot 重做工程

这是生存战争插件版的整包重做，不是演示。用 Godot 4.7 .NET 编辑器打开本目录的 `project.godot`。当前 Sdk 为 `Godot.NET.Sdk/4.7.0-beta.2`。

按运行会进入能走动、挖掘和放置的世界。模块完成度以 [docs/modules/README.md](docs/modules/README.md) 为准，不要把未完成的模块写成已完成。

## 改代码之前

1. 读 [docs/Decisions.md](docs/Decisions.md)。
2. 读 [docs/CodeMap.md](docs/CodeMap.md)。
3. 只读本次任务对应的 [docs/modules](docs/modules/README.md) 文件，读完再改代码。
4. 一个任务跨多个模块时，每个模块文档都要读。禁止只读总览就开工。

## 硬规则

- 交付范围是原游戏的全部玩法、界面和本地四人。不做 VR。施工顺序见 Decisions，顺序不等于可以删掉后面的模块。
- 本体逻辑用 C#。玩家能看见的界面用 Godot Control。禁止把玩法写成 GDScript。
- 模组用数据和 GDScript，而且只能通过该模块文档列出的回调。禁止 Harmony、Jint、运行时加载 C# 或原生库。
- 可以阅读仓库里的 `Engine/`、`EntitySystem/`、`Survivalcraft/` 来核对规则。禁止把这些项目引用进 `SurvivalcraftGodot.csproj`，禁止把旧类型复制进 `scripts/`。
- 禁止把本工程加入仓库根目录的 `SurvivalcraftApi.sln`。
- 新系统先改对应模块文档，再写代码。文档里没有的系统不准先造一个类。
- 禁止：GridMap、一格一个 MeshInstance3D、用 PhysicsServer 做地形碰撞、用 NavigationServer 做生物寻路、读旧 `Project.xml`、加载 `.scmod`、HTML 界面、商城、浏览器导出、四套独立键盘或鼠标。
- 脏区块在 C# 组网格，一次提交给 RenderingServer。
- 编辑器若改写了 `Godot.NET.Sdk` 的补丁版本，保留编辑器的结果，不要改回旧引擎的 `net10.0` 游戏工程。

## 文档索引

| 文件 | 作用 |
|------|------|
| [docs/Decisions.md](docs/Decisions.md) | 已锁定的决定，覆盖所有模块 |
| [docs/CodeMap.md](docs/CodeMap.md) | 代码只能放在哪些目录 |
| [docs/modules/README.md](docs/modules/README.md) | 功能模块列表 |
