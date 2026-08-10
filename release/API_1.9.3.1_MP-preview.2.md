# Survivalcraft API 1.9.3.1 Multiplayer Preview 2

Preview 2 将 API 插件版、完整联机核心和分层旧模组兼容宿主合并到同一发行线。发布标识为 `API_1.9.3.1_MP-preview.2`；联机协议修订仍为 17，因此本版没有引入新的 wire schema。Windows x64 客户端、Windows x64 Relay Server 与 Android ARM64 客户端来自同一源码提交和同一发布契约。

## 相比 Preview 1 的主要变化

- 中继主机在连接失败后会从中心服候选列表选择尚未尝试的节点，避免单个中继失效时立即终止建房。
- Android 自动测试使用独立 package 和数据目录，支持无坐标点击的新世界、重进、移动模式、模组清单、启动对话框和声明式世界探针；公开 APK 不包含测试自动化模组。
- 9 个真实 API 1.9.x 社区模组进入 SHA-256 锁定的单模组与显式组合矩阵，加载前还会只读检查宿主成员引用和 Harmony 目标歧义。
- 构千形、服饰配件和美好时代及其依赖完成经典 API 1.9 生命周期回归；这只证明能够与当前宿主和建房流程共存，不代表模组自定义玩法状态已经联机同步。
- 宿主新增分层旧 API 兼容：API 1.8 使用加法式二进制形状补齐，API 1.7 使用受限成员桥，API 1.4–1.6 只对已审计的确定性断点应用精确适配。
- 枪械 2.8 通过原始 `.scmod` SHA-256、包身份和程序集 SHA-256 三重锁定，在宿主内存中完成兼容迁移。原模组及其 DLL 不会被修改；新世界、退出重进、枪械数据、配方、地图及弹道创建/移除探针均已覆盖。

Preview 1 已完成的 M1–M13 联机核心、22/22 Coverage Contract、确定性丢包收敛、会话级模组通道隔离、隐藏 Direct/Relay 测试和独立 Relay Server 产物均继续保留。

## 产物

- `Survivalcraft-API-1.9.3.1-MP-preview.2-Windows-x64.zip`：Windows x64 客户端，依赖已安装的 .NET 10 Desktop Runtime。
- `Survivalcraft-RelayServer-1.9.3.1-MP-preview.2-win-x64.zip`：Windows x64 Relay Server，使用 `dotnet Survivalcraft.RelayServer.dll [port]` 启动。
- `Survivalcraft-API-1.9.3.1-MP-preview.2-Android-arm64.apk`：仅适用于 `arm64-v8a` Android 设备，最低 Android API 23；`versionCode` 为 19312。
- `SHA256SUMS.txt` 与 release manifest：用于核验下载完整性、源码提交、工具链、签名类型和 ABI。

## 安装与升级

- Windows 建议解压到新目录，不要覆盖 Preview 1 的程序文件。
- Android 使用与 Preview 1 相同的 package 和开发证书，可以直接升级安装；载入旧世界前仍应先备份。
- 所有参与联机的客户端应使用同一 Preview 2 版本。虽然 wire revision 保持 17，内置联机模组版本和兼容清单仍会参与握手校验。
- `.scmod` 可以从游戏“内容 → 设备”导入，随后完整重启游戏，再到“模组管理”检查启用状态、依赖和加载顺序。

## 已知限制

- DayZ 2.2.3.0 仍会在世界加载阶段因缺少旧宿主类型 `Game.SubsystemSeasons` 失败，本版不宣称支持 DayZ。
- 黑蝎 2.8 是依赖枪械的单人剧情附加模组，继续作为观察项，不纳入本版兼容承诺。
- 枪械 2.8 的本地生命周期和弹道探针通过，不代表其全部自定义状态已经在第二客户端之间同步。
- 美好时代社区说明标注暂不支持联机；本版只声明加载和世界生命周期兼容。
- 公网中心服和中继服由外部维护。发行门禁会验证本地独立 Relay 产物与协议，但无法保证第三方公网节点在任意时刻可用。
- Android APK 使用仓库开发证书签名；当前真机证据主要来自一台 Android 12 ARM64 设备，尚不能代表所有厂商、系统、GPU 和网络环境。

## 测试与安全

- 公开 Windows 和 Android 客户端不包含 `Survivalcraft.TestAutomation.scmod` 或 Android 自动化桥。
- Windows 启动、Direct 与 Relay 场景全部使用 `--headless` 和 `CreateNoWindow=true`，不会弹出游戏窗口。
- 第三方模组可以执行代码。请优先在无个人数据的测试设备和测试世界中验证，并保留 `Game.log`、设备型号、Android 版本和复现步骤。
