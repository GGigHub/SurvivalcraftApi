# 模组联机消息通道

API 联机版为第三方模组提供了稳定的命名空间化消息通道。模组不需要占用全局 `Packet` ID，也不应再把动态 `PacketManager.RegisterPacket` 当作跨模组联机协议；每条消息由模组包名、通道名、通道版本和唯一消息 ID 标识，并在玩家进入服务端世界前完成声明式协商。

## 最小示例

在 `ModLoader.__ModInitialize` 中注册通道，并保存返回的 `ModNetworkChannel`：

```csharp
using System.Text;
using Game;
using Game.Network;

public sealed class ExampleModLoader : ModLoader {
    private ModNetworkChannel channel;

    public override void __ModInitialize() {
        channel = RegisterNetworkChannel(
            channelName: "state",
            version: 1,
            directions: ModNetworkDirection.Bidirectional,
            handler: OnMessage,
            maximumPayloadBytes: 4096);
    }

    private void OnMessage(
        ModNetworkMessageContext context,
        ReadOnlyMemory<byte> payload) {
        string text = Encoding.UTF8.GetString(payload.Span);
        if (context.IsServer) {
            // 将客户端消息视为请求；先验证，再由服务端修改权威状态。
            ValidateAndApplyRequest(context.Sender, context.Project, text);
        }
        else {
            // 应用服务端下发的权威状态。
            ApplyServerState(context.Project, text);
        }
    }

    public override void ModDispose() {
        channel?.Dispose();
        channel = null;
    }
}
```

实际命名空间来自当前模组 `modinfo.json` 的 `PackageName`。例如包名是 `example.weather`、通道名是 `state`，线上标识就是 `example.weather:state`。包名和通道名会去除首尾空白并转为小写。

## 发送消息

客户端只能通过允许 `ClientToServer` 的通道向当前服务端发送：

```csharp
channel.SendToServer(payload);
```

服务端可以向一个已连接会话发送，或向全部兼容客户端广播：

```csharp
channel.SendToClient(session, payload);
int recipients = channel.Broadcast(payload);
int others = channel.Broadcast(payload, except: session);
```

发送前必须已经完成兼容握手并加载联机 Project。过早发送、方向不符、会话失效、通道已释放或载荷超限都会立即抛出明确异常。`Broadcast` 返回实际符合条件的接收者数量；没有接收者时返回 0。

`ModNetworkMessageContext` 提供：

- `IsServer`：当前处理器是否运行在服务端；
- `Sender`：服务端收到客户端请求时的认证会话；客户端处理服务端消息时该值为空；
- `Project`：当前仍然有效的 Project，不要在模组初始化时缓存旧 Project；
- `Channel`：本地通道声明的只读语义副本；
- `MessageId`：本次逻辑消息的唯一 ID，可用于业务日志和幂等处理。

处理器在游戏主线程的网络更新阶段执行，应保持短小，不要做阻塞 I/O。处理器抛出的异常会被记录并隔离，本次消息会被丢弃，但网络会话不会因此终止。

## 声明与握手规则

服务端和客户端会在核心 API/模组清单通过后交换规范化通道目录。双方必须对每个通道给出完全相同的：

1. 包名和通道名；
2. 正整数通道版本；
3. 允许方向；
4. 单消息最大载荷。

缺失、多出、版本不同、方向不同或载荷上限不同都会在 Project 下发前拒绝连接，并返回稳定错误码。当前策略有意采用严格相等；尚未定义“客户端可选”或“仅服务端存在”的通道类型。

因此，通道注册必须是确定性的：不要根据当前世界、玩家设置、随机数或客户端/服务端角色决定是否注册。协议发生破坏性变化时增加通道版本，并保证服务器与客户端一起更新。只增加可选字段且旧处理器能够安全忽略时，可以维持版本不变。

## 限制与保护

| 项目 | 限制 |
| --- | ---: |
| 全部通道 | 512 |
| 每个模组包 | 32 |
| 包名长度 | 256 字符 |
| 通道名长度 | 96 字符 |
| 默认单消息载荷 | 16 KiB |
| 单消息全局上限 | 64 KiB |
| 编码后目录 | 256 KiB |
| 每个对端每秒消息 | 128 |
| 每个对端每秒载荷 | 512 KiB |
| 每个对端保留的去重 ID | 1024 |

标识符只允许字母、数字、点、短横线和下划线。空 GUID、非法版本、未知方向、尾随数据、截断数据和越界长度都会在进入模组处理器前被拒绝。限流和去重按认证会话隔离；断线、服务端会话移除和 Project 释放时会清理相应状态。

底层直连和中继传输都使用可靠有序发送，但模组仍应采用幂等设计：处理器可能因限流、验证失败或异常而拒绝一条消息，应用层不会自动重试业务操作。对“设置状态”类消息优先发送完整期望状态或带业务序号的命令，不要依赖无法辨别是否重复执行的增量副作用。

## 权威模型

通道只解决安全分发和生命周期问题，不会自动把单机模组变成联机模组。推荐固定使用以下模型：

```text
客户端输入/意图
    -> ClientToServer 请求
    -> 服务端校验身份、权限、距离、物品和世界状态
    -> 服务端修改权威 Project
    -> ServerToClient 状态或事件
    -> 客户端只更新表现和缓存
```

不要信任客户端提交的玩家索引、实体所有权、坐标、物品数量或权限。使用 `context.Sender` 关联的服务端会话查找玩家，并在服务端重新验证所有会改变世界的条件。纯视觉数据也应设置合理长度，避免把消息通道当文件传输接口。

## 生命周期

- 通道在模组加载期注册，跨世界加载和世界重载持续存在；
- `GameManager.DisposeProject` 只清理对端限流和去重状态，不删除模组注册；
- 客户端断线、直连会话移除和中继会话移除都会清理对应状态；
- 晚加入或重连的客户端必须在自己的新 session 中重新协商完整目录；旧 session 的目录、限流和去重状态不会继承；
- 模组实体释放时，即使某个 `ModDispose` 抛出异常，该加载器拥有的通道仍会在 `finally` 中注销；
- `ModsManager.Initialize` 重新建立整个模组运行时时会清空旧注册；
- `ModNetworkChannel.Dispose` 可提前注销单个通道，重复调用是安全的。

不要动态增删通道；当前目录只在连接握手时协商。为避免既有会话与新会话看到不同协议，联机传输启动后新增注册会被拒绝。`Dispose` 仍允许在模组卸载时立即移除处理器；卸载或减少通道后必须重新建立联机连接。

## 自动测试

仓库的测试模组注册了 `survivalcraft.testautomation:automation` 双向通道，并通过强类型控制协议验证真实消息路径。常用门禁：

```powershell
./scripts/Test-Multiplayer.ps1 -Mode fast
./scripts/Test-Multiplayer.ps1 -Mode core
./scripts/Test-Multiplayer.ps1 -Mode relay
./scripts/Test-Multiplayer.ps1 -Mode session
./scripts/Test-Multiplayer.ps1 -Mode full
```

`core` 验证直连消息、重复包去重、处理器异常隔离、断线重连和世界重载；`relay` 启动独立隐藏中继进程，验证相同目录协商、双向消息、故障注入、会话清理和同房间重连；`session` 在 Direct/Relay 各运行一服四客，验证晚加入目录、逐会话限流/恢复、背压、异常隔离、并发重连和最终状态清理。所有真实游戏节点都强制使用 `--headless`、独立 `--data-dir` 和 `CreateNoWindow=true`。

失败与成功都会写出 `steps.json`、`metrics.json`、快照、节点日志和 `replay.json`。开发模组协议时，应先为纯编解码和边界添加 L0/L1，再增加一个使用真实 Project 的无界面 L2 场景。

更完整的测试入口和产物说明见 [Testing.md](Testing.md)，集成分支协议决策见 [API-Multiplayer-Integration.md](API-Multiplayer-Integration.md)。
