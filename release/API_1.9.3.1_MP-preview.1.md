# Survivalcraft API 1.9.3.1 Multiplayer Preview 1

这是 API 插件版与联机核心合并后的首个开发者预览版。发布标识为 `API_1.9.3.1_MP-preview.1`，联机协议修订为 17；Windows x64 客户端、Windows x64 Relay Server 与 Android ARM64 客户端来自同一源码提交和同一发布契约。

## 本版目标

本版优先验证“完整核心玩法能够联机、在故障后收敛、API 模组通道按会话隔离”。它不是正式稳定版，不承诺任意第三方模组、所有公网 NAT、全部移动设备图形驱动或像素级 UI 行为已经验证。

## M1–M13 已完成内容

- M1：玩家放置、挖掘、使用，以及移动、视角和蹲伏进入强类型动作、快照、权威校验、重连与持久化自动验收。
- M2：地形流送与修订、实体和生物生命周期具备 Direct/Relay、late join、重连、乱序与保存重载证据。
- M3：背包、容器、生命/伤害/重生、可拾取物和投射物完成服务端权威同步与幂等验证。
- M4：时间、天气、睡眠、火焰/爆炸、坐骑、电路逻辑及移动方块进入可回放的自动化场景。
- M5：建立 Coverage Contract v2、生产路径证明、资源预算、故障回放与版本化 legacy 失败清单，使自动化证据默认 fail closed。
- M6：完成握手兼容矩阵、玩家会话生命周期、稳定身份、限流与 API 模组消息通道。
- M7：完成容器所有权/距离校验及真实熔炉投料、燃料、产出、late join、重连和保存重载。
- M8：完成世界火焰、爆炸事件及其修订/墓碑同步，并补齐基线同步与挖掘恢复路径。
- M9：家具锤使用改为服务端权威，校验身份、手持物、耐久、距离、源方块、设计和材料，并按请求 ID 去重。
- M10：创造飞行具备权威请求/结果、生命周期修订、拒绝修复、late join、重连与持久化。
- M11：生物行为/本地声音、衣物/自定义皮肤、流感/疾病、成长统计与生命指标四域完成同步。
- M12：不可替代业务包使用有界应用层 Data/ACK、指数退避和幂等投递；Direct 与 Relay 十二场景均通过确定性 1–3% 丢包验收。
- M13：第三方模组支持 Required、Optional、LocalOnly 安装范围；每个会话只协商双方共有通道，所有发送、广播和接收路径都执行隔离。

当前 Coverage Contract 中 22 个核心无头联机 feature 全部为 `covered`；Automation API 为 16，Snapshot schema 为 5。

## 产物

- `Survivalcraft-API-1.9.3.1-MP-preview.1-Windows-x64.zip`：Windows x64 客户端，依赖已安装的 .NET 10 Desktop Runtime。
- `Survivalcraft-RelayServer-1.9.3.1-MP-preview.1-win-x64.zip`：Windows x64 Relay Server，使用 `dotnet Survivalcraft.RelayServer.dll [port]` 启动。
- `Survivalcraft-API-1.9.3.1-MP-preview.1-Android-arm64.apk`：仅适用于 `arm64-v8a` Android 设备，最低 Android API 23。
- `SHA256SUMS.txt` 与 release manifest：用于核验下载完整性、源码提交、工具链和 ABI。

## 测试与安全说明

- 公开客户端产物不包含 `Survivalcraft.TestAutomation.scmod`；发布烟测只向一次性解压副本注入该模组。
- Windows 启动、Direct 与 Relay 场景全部以 `--headless`、`CreateNoWindow=true` 在隐藏窗口运行。
- Android APK 使用仓库开发者密钥签名，适合本轮设备测试，不代表应用商店或最终正式版签名。
- 安装或载入旧世界前请先备份。协议修订 17 要求参与联机的客户端和服务端使用兼容版本。

## 仍需真实设备验证

自动化尚不能替代 Android 真机图形、触控、系统权限、后台切换、功耗和不同厂商网络栈测试，也不能证明任意公网路由器/NAT 与所有第三方模组都兼容。遇到问题请保留 `Game.log`、设备型号、Android 版本、连接方式和复现步骤。
