# VR

代码目录：`scenes/vr/` 放 XR 原点，逻辑在 `scripts/Vr/`。

原项目：自研 OpenXR。ANGLE 回退时 VR 不可用，因为上下文不是 WGL。

## 必须

- 使用 Godot OpenXR。Windows 和安卓（包括头显）要能进入 VR 模式。
- 双手控制器能挖掘、放置和打开简化后的背包。映射可以在设置里改。
- VR 里的世界仍是同一套区块网格和格子碰撞。
- 不支持 VR 的机器走普通画面，不能阻止游戏启动。

## 禁止

- 把 `Engine/VR/OpenXrVrBackend.cs` 和 Silk.NET OpenXR 引用进本工程。
- 为 VR 另做一套方块渲染。
- 在 iOS 上承诺 VR。

## 完成标准

在有 OpenXR 运行时的 Windows 上能以头显观看并用手控挖掘一格。没有头显时，游戏以普通窗口启动。
