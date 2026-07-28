# 联机测试基线（2026-07-28）

## 起点

- 母分支：`origin/SCAPI1.9_MP`
- 起点提交：`b9deb57570ec2e3898b6f63b70a56aaab313c2dd`
- 本地环境：Windows、.NET SDK 10.0.301、.NET Runtime 10.0.9；仓库根目录的 `global.json` 固定 10.0.3xx 特性带并允许补丁前滚。
- 基线构建：`Survivalcraft.Test` Debug 构建成功，17 个既有警告、0 个错误，约 17.6 秒（首次还原后的干净定向构建）。
- 基线发现：124 个既有 `[Fact]` 测试。
- 快速既有样本：`NetworkBodyInterpolationTest` 2/2 通过，测试阶段约 0.84 秒。

## 基线问题

原测试的功能覆盖已经较多，但自动开发反馈受以下结构限制：

- `GameFixture` 全局最多并发 1 组，常规测试会启动两个或三个可见游戏进程。
- 每个角色复制完整运行目录，单份约 49 MiB、331 个文件。
- 大量场景通过日志字符串判断状态，并依赖 `WaitLog`、真实等待时间和 UI 点击。
- 旧控制面是 UDP 文本命令，复杂操作依赖任意 Roslyn `EXEC`/`FUNC` 脚本，缺少版本、类型和稳定错误码。
- 失败时没有统一快照、状态差异、指标和回放参数，AI 很难仅凭一行超时定位原因。
- 仓库没有面向核心联机回归的自动 PR 门禁。

这些问题不代表既有测试无价值；它们适合作为功能资产逐步迁移，但不适合作为每次 AI 修改后的唯一反馈环。

## API 联机里程碑验收数据

在 `codex/api-mp-integration` 上合入 SCAPI 1.9.3 并完成兼容握手后，同一机器、Debug 配置下的最新结果为：

| 验证 | 结果 | 时间 |
| --- | --- | --- |
| L0/L1 `Survivalcraft.Testing.Tests` | 20/20 通过 | 秒级 |
| TestHost doctor | 模组、控制通道与 `Survivalcraft.Networking.dll` 均通过检查 | 未单独计时 |
| 五类兼容矩阵 | 5/5 通过 | 场景内部 23.475 秒 |
| 双进程核心场景 | 15/15 步通过 | 场景内部 11.576 秒 |
| 标准 xUnit L2 门禁 | 2/2 通过 | 总时间 36.1497 秒 |
| 真实中继纵向用例 | 1/1 通过，结束后零残留进程 | 测试总时间 32.1473 秒 |

核心场景验证玻璃方块值 `15` 在服务端、首次客户端、重连客户端和保存后重载服务端之间一致，持久化差异为零。兼容矩阵分别稳定返回 `ProtocolMismatch`、`ApiVersionMismatch`、`MissingCapabilities`、`ExtraClientMod` 和 `HandshakeTimeout`，清除故障后可以在同一进程对中恢复联机。中继用例覆盖房间连接、兼容握手、项目传输、玩家进入、初次地形与后续服务端权威地形变更。

所有真实游戏进程均通过 `--headless`、`CreateNoWindow=true` 和独立 `--data-dir` 在后台运行，不弹出游戏窗口，也不复用用户日常世界、设置、日志或缓存目录。

历史测试集合在迁移审计中仍发现若干与本里程碑无关的脆弱断言，包括 UI 操作链、日志文本和动态生物位置容差。已修复旧中继脚本命名空间、地形诊断字段和离线世界重载空引用；尚未迁移的历史用例不计入当前稳定门禁，后续按业务域逐批替换为强类型状态断言。

## 当前可重复命令

```powershell
./scripts/Test-Multiplayer.ps1 -Mode fast
./scripts/Test-Multiplayer.ps1 -Mode doctor
./scripts/Test-Multiplayer.ps1 -Mode compatibility
./scripts/Test-Multiplayer.ps1 -Mode core
./scripts/Test-Multiplayer.ps1 -Mode full
```

稳定门禁的成功标准：命令退出码为 0；L0/L1 全绿；doctor 到达 `MainMenuScreen`；兼容矩阵五类结果与预期一致；核心 L2 的 `outcome.json` 为 `passed` 且 `state.diff.json` 的 `equal` 为 `true`。`full` 当前用于历史迁移审计，不等同于全绿承诺。

## 后续迁移指标

后续开发不以“测试数量越多越好”为唯一目标，而观察：

- PR 必跑反馈时间；
- 无固定睡眠的场景占比；
- 不依赖 Roslyn 脚本的场景占比；
- 失败后能生成结构化快照和回放配置的场景占比；
- 相同种子连续运行的通过率；
- L2 核心场景的 P50/P95 耗时。

下一批优先迁移顺序建议为玩家移动、背包与掉落物、实体生成/销毁、区块边界交互，最后再迁移依赖中继服务和复杂 UI 的长场景。
