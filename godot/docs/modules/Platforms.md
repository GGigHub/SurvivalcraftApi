# 平台

原项目平台：Windows、Linux、安卓、iOS、浏览器。本工程的交付平台以本文件为准。

## 必须

- Windows 和 Linux 用同一套 C#，正式交付。
- 安卓正式交付，但导出仍按 Godot .NET 的实验性能力处理：只出 `arm64-v8a`，目标框架按 csproj 的安卓条件使用 `net9.0`。世界骨架第一次能挖方块时就要在真机打一次包，不能等到全部模块写完。
- 渲染器按 [Settings.md](Settings.md)。安卓工程设置保持 `rendering_method.mobile`。
- 模组包在这三个平台通用，见 [Mods.md](Mods.md)。

## 禁止

- 做浏览器导出，或把本体改成 GDScript 只为了上网页。
- 把 iOS 写成和 Windows 同级的完成标准。要单独立项时，先改 [Decisions.md](Decisions.md) 和本文件。
- 在本工程引用 Silk.NET、GLFW、SDL、ANGLE 或旧的 `Engine.Android`。

## 完成标准

Windows 和 Linux 能启动并进入一局。安卓 arm64 包能启动并进入同一套玩法。仓库里没有浏览器导出预设。iOS 预设如果存在，必须在本文件标明「不交付」。
