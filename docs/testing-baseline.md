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

## 逻辑/电力/移动方块主线验收（2026-07-30）

`logic.electricity-and-moving-blocks` 已使用同一个强类型 17 动作场景完成 Direct、Relay 及二者严格 replay 四次真实进程验收，四次均通过。场景固定一服三客、seed `20260805`，在 100% 复制和 50% 重排下覆盖九类可编辑逻辑方块、真实开关交互、电力 revision、moving-block instance ID/generation/tombstone、相同业务 ID 的无标签集合并发、活塞生命周期、观察者、late join 预载对账、同身份重连及保存重载。每次运行均产生 8 条结构化业务断言、Snapshot v2、动作、事件、网络故障和回放产物；全程继续使用无窗口运行时。

历史测试集合在迁移审计中仍发现若干与本里程碑无关的脆弱断言，包括 UI 操作链、日志文本和动态生物位置容差。已修复旧中继脚本命名空间、地形诊断字段和离线世界重载空引用；尚未迁移的历史用例不计入当前稳定门禁，后续按业务域逐批替换为强类型状态断言。

## M6 会话与 API 通道验收（2026-07-30）

M6 在同一 Debug/ANGLE 运行时上完成全部无窗口验收：平台 L0/L1 109/109、游戏 L0/L1 68/68、完整游戏 L2 25/25；其中握手阶段兼容矩阵、Direct 生命周期和 Relay 生命周期均通过。两个生命周期传输各返回 5 类兼容拒绝、4 个稳定玩家身份、18 次重复 `PlayerCreatePacket` 抑制和 32 次逐会话限流。额外 session-only Direct L3 使用 seed `20260730` 与 loss profile 通过，五节点峰值工作集约 471–505 MiB，强制 `PlayerCreatePacket` 丢包哨兵实际记录 `faultDropped >= 1` 且最终待发队列为零。

并发重连首次真实暴露了 Project 尚未加载时处理 `PlayerAddPacket` 的空引用。修复后的客户端按 session 延迟依赖 Project 的包，并以 4096 项硬上限阻止无界增长；断线和 session 切换会清空旧队列。覆盖契约因此把 `network.session-compatibility`、`api.mod-channel` 和 `player.session-lifecycle` 从 `partial` 提升为 `covered/soak`，当前计数为 14 covered、2 partial、5 planned。

## M7 熔炉与容器验收（2026-07-30）

M7 将容器开启结果、所有权/距离拒绝、真实熔炉投料、燃料消耗、熟食产出、独立 furnace revision、late join、同身份重连和保存重载合并进严格十四动作场景。最终无窗口门禁为平台 L0/L1 110/110、游戏 L0/L1 73/73、完整游戏 L2 25/25；Direct 与 Relay 的一服三客定点 Theory 另行 2/2 通过。额外 Direct L3 使用 seed `20260730`，在延迟、抖动、100% 重复、随机重排和带宽调度下通过，四节点峰值工作集约 406–454 MiB，句柄峰值 901、线程峰值 36，最终故障待发队列为零。所有节点继续使用 `--headless`、`CreateNoWindow=true` 与隔离数据目录。

覆盖契约由此把 `inventory.furnace-container` 从 `partial` 提升为 `covered/mutation`，当前计数为 15 covered、1 partial、5 planned；生产面精确库存为 104 个 hook 和 110 个 packet class。首轮完整 L2 在并行负载下捕获到“槽位内容已相同但客户端 revision 尚未到达最终值”的等待竞态；加入最终 revision 下限后，定点 2/2 与最终完整 L2 25/25 均通过，门禁不再把内容巧合相等误判为版本收敛。

## 当前可重复命令

```powershell
./scripts/Test-Multiplayer.ps1 -Mode fast
./scripts/Test-Multiplayer.ps1 -Mode doctor
./scripts/Test-Multiplayer.ps1 -Mode compatibility
./scripts/Test-Multiplayer.ps1 -Mode core
./scripts/Test-Multiplayer.ps1 -Mode session
./scripts/Test-Multiplayer.ps1 -Mode soak -SoakIterations 10
./scripts/Test-Multiplayer.ps1 -Mode full
```

稳定门禁的成功标准：命令退出码为 0；L0/L1 全绿；doctor 到达 `MainMenuScreen`；兼容矩阵五类拒绝与六个阶段黑洞结果符合预期；核心 L2 的 `outcome.json` 为 `passed` 且 `state.diff.json` 的 `equal` 为 `true`；session Direct/Relay 的身份、重复创建、限流、背压和最终清理断言全部通过。`full` 当前用于历史迁移审计，不等同于全绿承诺。

## 后续迁移指标

后续开发不以“测试数量越多越好”为唯一目标，而观察：

- PR 必跑反馈时间；
- 无固定睡眠的场景占比；
- 不依赖 Roslyn 脚本的场景占比；
- 失败后能生成结构化快照和回放配置的场景占比；
- 相同种子连续运行的通过率；
- L2 核心场景的 P50/P95 耗时。

下一批优先迁移顺序建议为玩家移动、背包与掉落物、实体生成/销毁、区块边界交互，最后再迁移依赖中继服务和复杂 UI 的长场景。
