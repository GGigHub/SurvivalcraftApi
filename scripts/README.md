# 仓库脚本

请在解决方案根目录运行本目录中的脚本。

## 联机自动化

`Test-Multiplayer.ps1` 提供 fast、trust、doctor、Direct/Relay 场景、soak、legacy 和 full 门禁。所有由测试框架启动的游戏与 Relay 进程均为无头、隐藏窗口进程。

## Android 真机无人值守测试

```powershell
./scripts/Test-AndroidLocal.ps1 -DeviceId <adb-serial>
```

总入口构建独立 ARM64 测试 APK，执行休眠唤醒/设置恢复、ADB 故障恢复、Pad 新建与重进世界联机回归，以及哈希锁定的中文社区模组单体/组合矩阵。单层入口为 `Build-AndroidDeviceTest.ps1`、`Test-AndroidDevice.ps1`、`Test-AndroidDeviceStability.ps1`、`Resolve-AndroidModCorpus.ps1` 和 `Test-AndroidModMatrix.ps1`。参见 [AndroidDeviceTesting.md](../docs/AndroidDeviceTesting.md)。

## API 联机预览发布

```powershell
./scripts/Build-MultiplayerPreview.ps1
./scripts/Test-MultiplayerPreviewRelease.ps1
./scripts/Publish-CnbMultiplayerPreview.ps1
```

- `Build-MultiplayerPreview.ps1`：从 `release/multiplayer-preview.json` 构建 Windows x64、Relay win-x64 和 Android ARM64 APK，生成 manifest 与 SHA-256 清单。默认要求 tracked worktree 干净；`-AllowDirty` 只供本地预检。
- `Test-MultiplayerPreviewRelease.ps1`：验证哈希、APK 签名/ABI/版本/内置模组，并从全新解压目录执行隐藏启动、Direct 与独立 Relay 烟测。`-StaticOnly` 不启动游戏。
- `Publish-CnbMultiplayerPreview.ps1`：要求干净 manifest、匹配 HEAD 的注释标签和远端标签；使用 `CNB_TOKEN` 或 Git Credential Manager 中的 CNB token 创建/更新 prerelease、上传并复核所有附件。脚本不会输出 token。
