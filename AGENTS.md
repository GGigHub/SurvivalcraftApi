# Survivalcraft API 生存战争插件版

## 本分支是 Godot 整包重做

当前分支 `feat/godot-port` 在保留原引擎源码的同时，新增根目录下的 [godot/](godot/) 工程。移植、重做、新玩法只改 `godot/`。开始前阅读 [godot/AGENTS.md](godot/AGENTS.md)。禁止把 `Engine/`、`EntitySystem/`、`Survivalcraft/` 抄进 Godot 工程，禁止把重做收成可玩演示后停工，禁止读旧存档或加载 `.scmod`。

以下段落只在维护尚未重做的插件版源码时使用。

# Survivalcraft API 生存战争插件版

这是生存战争插件版（Survivalcraft API）游戏的源码仓库  

如果你需要参考本仓库，或者为本仓库贡献源码，请直接阅读 [docs/Architecture.md](docs/Architecture.md) 架构文档即可

如果你需要为本游戏制作/开发模组（Mod），请先确认你正在开发的模组项目是否是新键项目/空项目  
如果是新建项目/空项目，请检查你的工作目录是否有生存战争插件版示例模组源码，即 [SC-SPM/SurvivalcraftTemplateModForAPI](https://gitee.com/SC-SPM/SurvivalcraftTemplateModForAPI)  
如果没找到该示例模组源码，请自行完成 git clone，并阅读其中的 `AGENTS.md`，按照其指导制作模组

如果正在开发的模组项目不是新键项目/空项目，请阅读 [docs/Development.md](docs/Development.md) 开发文档，接着阅读 [docs/Architecutre.md](docs/Architecture.md) 架构文档，作为开发模组的参考