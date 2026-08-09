# Android 真机无人值守测试

本分支提供独立于正式包的 Android ARM64 自动测试 APK。它使用固定命令白名单和文件式 JSON 协议控制游戏，不使用游戏画面坐标、OCR 或逐图点击；Windows 侧不会启动桌面游戏窗口。测试包、数据目录和正式包完全分离：

| 项目 | 正式包 | 自动测试包 |
|---|---|---|
| Android package | `com.candy.survivalcraftAPI1_9` | `com.candy.survivalcraftAPI1_9.autotest` |
| 外部数据目录 | `/sdcard/Survivalcraft2.4_API1.9` | `/sdcard/Survivalcraft2.4_API1.9_AutoTest` |
| 自动化模组 | 不包含 | 条件编译后内置 |
| 自动化协议 | 不存在 | `AutoTest/inbox`、`responses`、`processed` |

## 默认长跑

连接并授权一台 ARM64 Android 设备后，在仓库根目录运行：

```powershell
./scripts/Test-AndroidLocal.ps1 -DeviceId <adb-serial>
```

默认流程会依次执行：

1. L0 发布隔离、语料锁和中继候选门禁；
2. 构建独立测试 APK；
3. 安装检查、语义桥 doctor 和一次可证实的 ADB daemon 故障恢复；
4. 三轮 Pad 移动模式的新建世界与五次已有世界重进，共 18 次真实中继建房；
5. 中文社区模组的单模组隔离和显式兼容组矩阵；
6. 清空测试世界与测试模组，确认测试进程退出并恢复设备电源设置。

开发期缩短运行可复用 APK 和已锁定语料：

```powershell
./scripts/Test-AndroidLocal.ps1 `
  -DeviceId <adb-serial> `
  -Cycles 1 `
  -ReentryCount 1 `
  -NoBuild `
  -NoInstall `
  -OfflineCorpus
```

`-NoInstall` 只应在测试包已经安装时使用。`-OfflineCorpus` 要求 `artifacts/android-mod-corpus/packages` 中已有与锁文件一致的包。

## 无人值守设备治理

每个设备用例都会记录测试前状态，唤醒设备，通过 `wm dismiss-keyguard` 解除无需凭据的锁屏，并在 USB 供电期间临时启用防休眠。用例结束后会恢复原始 `stay_on_while_plugged_in` 值；测试开始前设备处于休眠时，还会恢复休眠状态。恢复失败会让用例失败，而不是只写警告。

框架不会尝试绕过 PIN、密码或生物识别。专用测试机存在安全锁时，需要先人工解锁一次。华为设备若启用了“使用 ADB 安装应用时，需要用户确认”，安装前置检查会通过系统设置的语义节点关闭该选项，同时保持系统包验证器开启；可用 `-SkipDeviceConfiguration` 禁止这项调整。

## 分层入口

- `Build-AndroidDeviceTest.ps1`：构建并静态检查独立 package 的 ARM64 测试 APK。
- `Test-AndroidDevice.ps1`：单次 doctor 或 `relay-reentry-pad`；支持外部 `.scmod` 精准装载、预期包名检查、ADB 故障注入和失败归因。
- `Test-AndroidDeviceStability.ps1`：构建一次后重复 doctor、故障恢复和 Pad 中继重进。
- `Resolve-AndroidModCorpus.ps1`：直接连接下载、SHA-256 锁定、压缩包安全检查、解码和 `modinfo.json` 校验。
- `Test-AndroidModMatrix.ps1`：空环境基线、每个模组单独运行、显式兼容组运行、空环境清理。
- `Test-AndroidLocal.ps1`：串联以上门禁并生成一个总汇总。

所有轮询都等待结构化状态并设有截止时间。中继传输失败会尝试当前会话中尚未使用的候选端点；ADB offline、daemon 重启或连接中断会执行有界恢复。每个成功用例也会拉取 `Bugs/Game.log`，任何 `ERROR`、`FATAL` 或未处理异常都会使门禁失败，避免把被游戏捕获的模组异常误判为成功。每次运行都会 `force-stop` 测试 package，不会在电脑桌面留下游戏窗口。

## 中文社区模组语料

可审查的语料定义位于：

- `tests/android-mod-corpus/manifest.json`：来源 URL、大小、预期包名/版本和测试组合；
- `tests/android-mod-corpus/lock.json`：实际 SHA-256、最终 URL、包元数据、依赖和归档统计。

当前阻断门禁包含以下真实 API 模组。版本列以包内 `modinfo.json` 为准，而不是社区文件名：

| 语料 ID | PackageName | 包内版本 / API | 主要覆盖面 |
|---|---|---|---|
| `survivalcraft-ruins-0.4.0` | `SurvivalcraftRuins` | `0.4.0 / 1.9.2` | 世界生成与结构资源 |
| `survivalcraft-travel-map-20260805` | `SurvivalcraftTravelMap` | `1.0.0 / 1.9.2.1` | 地图状态与 UI |
| `better-vanilla-1.1a` | `MainUIButtonsMod` | `1.0.0 / 1.9.3.0` | 原版界面与行为钩子 |
| `fragile-body-0.2.7` | `Fragile body` | `0.1. / 1.9.2.1` | 玩家组件与分部位生命 |
| `kill-report-1.2` | `KillReport` | `1.2 / 1.9.3` | 击杀事件、音频和 UI |
| `time-rewind-20260807` | `timerewindmod.TimeRewind` | `1.0.0 / 1.9.3` | 玩家状态与时间回溯 |
| `transmutation-dye-1.11` | `captey.sbrsj` | `1.1 / 1.9.3.1` | 配方、物品和资源定义 |
| `blueprint-1.0.0` | `lantudapao` | `1.00 / 1.9.3` | 投影、背包和配置 UI |
| `seven-deadly-sins-0.2` | `SevenSins` | `0.1 / 1.9.3.1` | 实体 AI、战斗和掉落 |

其中部分社区文件名版本高于包内版本，这是上游元数据现状；清单会锁定真实包内值，防止后续文件在同名 URL 下静默漂移。每个模组有且仅有一个单体用例，另有 UI/信息四模组栈和玩法五模组栈用于发现钩子、资源及注册冲突。

新增模组的标准流程：

1. 从游戏当前使用的中文社区列表取得版本化 HTTPS 下载地址；
2. 在 `manifest.json` 增加条目，并为它增加且仅增加一个单模组用例；
3. 依赖模组必须进入同一用例，兼容性组合必须显式列出；
4. 运行并审查锁文件：

```powershell
./scripts/Resolve-AndroidModCorpus.ps1 -UpdateLock
./scripts/Resolve-AndroidModCorpus.ps1 -Offline
./scripts/Test-AndroidModMatrix.ps1 -DeviceId <adb-serial> -Offline
```

解析器仅允许清单白名单中的 HTTPS 主机，并强制下载大小、ZIP 条目数、展开体积和压缩比上限；拒绝路径穿越、规范化后的重复路径、缺失根级 `modinfo.json`、保留包名和元数据漂移。普通运行必须匹配锁定 SHA-256，同一 URL 被替换也会立即失败。社区常见的 `[]` URL 文件名和 Windows ZIP 反斜杠会先安全规范化，再执行同一套检查。

第三方模组是可执行代码。即使框架做了压缩包检查，也不能证明其行为安全；社区语料只应在无个人数据的专用设备或模拟器中运行。下载包保存在 Git 忽略的 `artifacts/`，仓库只提交来源和哈希锁。

## 诊断材料

总结果位于 `TestResults/android-local/<run>/summary.json`。子目录保留各层汇总与每次协议请求/响应。失败用例还会生成：

- `failure-logcat.txt`：本次启动后清空并重新采集的 Android 日志；
- `failure-exit-info.txt`：Android `ApplicationExitInfo`；
- `failure-screen.png`：只在失败时采集，不参与自动判断；
- `game.log`：每个成功或失败用例拉取的游戏日志；成功门禁要求没有 `ERROR`、`FATAL` 或未处理异常；
- `state-timeline.json`：屏幕、世界、玩家、中继端点/尝试次数和联机码时间线；
- `summary.json/failureArtifacts`：最后命令、失败阶段、进程是否存活及分类。

常见分类包括启动期进程退出、自动化桥不可达、模组清单拒绝、世界运行期退出和断言/超时。矩阵会继续执行后续模组，并把失败归因到包含它的单模组或组合用例。

## 公开包隔离

`EnableAndroidDeviceTestAutomation` 默认是 `false`，测试数据路径只存在于 `ANDROID_DEVICE_TEST_AUTOMATION` 条件编译块。公开预览构建显式传入 `false`，构建脚本与发布验证脚本都会扫描 APK 条目并拒绝任何 `TestAutomation` 资产。`ReleaseContractTests` 还会检查这一条件链、测试 package/data 分离、命令桥不包含动态 C#、进程启动或程序集加载入口。

## 当前边界

真机框架已经覆盖启动、存储、Pad 设置、新世界/已有世界建房、重复运行、ADB 恢复、中继故障转移和代表性社区模组加载；它仍不等同于第二台 Android 客户端实际入房、跨运营商 NAT、像素级渲染验收、长时间温控/电耗或每个模组玩法动作。例如蓝图投影、染色剂合成、击杀播报和七宗罪战斗仍需要后续声明式专用探针。多人玩法协议的确定性 Direct/Relay 覆盖继续由 `Test-Multiplayer.ps1` 的无窗口多进程场景负责。
