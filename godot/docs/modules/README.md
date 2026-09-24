# 功能模块

改某个模块之前只读对应文件。模块都在交付范围内。状态一栏在开始写该模块的代码时改为「进行中」，达到该文件的完成标准后改为「已完成」。现在全部是「未实现」。

| 模块 | 文件 | 状态 |
|------|------|------|
| 输入动作 | [Input.md](Input.md) | 进行中：动作已接入，设置里改键还没有 |
| 地形 | [Terrain.md](Terrain.md) | 进行中：能加载区块并挖放，形状只有实心方块 |
| 光照 | [Lighting.md](Lighting.md) | 进行中：阳光按柱往下，并横向漏进洞穴。火把、岩浆和点亮的灯照亮周围。昼夜只压阳光，不压方块光 |
| 世界生成 | [WorldGen.md](WorldGen.md) | 进行中：草、沙、砂砾、雪、冰、花岗岩和煤铜铁矿。还不是原版完整噪声 |
| 流体 | [Fluids.md](Fluids.md) | 进行中：水和岩浆会流。水里变慢，头埋进水里会溺水。有桶。岩浆掉血 |
| 方块 | [Blocks.md](Blocks.md) | 进行中：编号和掉落已对照原版 BlocksData。画面仍是纯色，多数方块还不能放 |
| 电路 | [Electricity.md](Electricity.md) | 进行中：导线、按钮、灯、与或非、活塞和火药桶可放进世界并通电。延迟和锁存还没有 |
| 移动方块 | [MovingBlocks.md](MovingBlocks.md) | 未实现 |
| 家具 | [Furniture.md](Furniture.md) | 未实现 |
| 衣物 | [Clothing.md](Clothing.md) | 未实现 |
| 生物 | [Creatures.md](Creatures.md) | 进行中：吃草动物、鸟、会追人的狼。打死掉肉。人和生物会推开。没有骑乘、船和图鉴其余物种 |
| 寻路 | [Pathfinding.md](Pathfinding.md) | 进行中：动物沿地面格子迈步。没有完整 A* |
| 碰撞与移动 | [Bodies.md](Bodies.md) | 进行中：玩家对格子碰撞，并和生物互相推开。树叶挡人，草丛和火把放置射线穿过 |
| 战斗与粒子 | [Combat.md](Combat.md) | 未实现 |
| 时间与环境 | [Environment.md](Environment.md) | 进行中：昼夜亮度。春雨、夏雷、秋短雨、冬雪，其余时间晴。季节会改体温。没有闪电劈中的单独动画 |
| 生存属性 | [Survival.md](Survival.md) | 进行中：饥饿、生命、湿、体温、生病、吃肉、睡觉、溺水、等级。挖和打猎加经验。没有完整疾病种类 |
| 物品与容器 | [Items.md](Items.md) | 进行中：背包、配方、桶、箱子、熔炉、发射器。铜镐挖方块会掉耐久，挖到上限会坏 |
| 音频 | [Audio.md](Audio.md) | 未实现 |
| 界面 | [Ui.md](Ui.md) | 进行中：主菜单可进入世界、设置和退出。游戏内背包仍是文字面板 |
| 存档 | [Saves.md](Saves.md) | 进行中：主菜单可以新开或继续。按 Esc 写入 user://saves/world.scg，记下区块、背包、位置和生命。拒绝旧 Project.xml |
| 模组 | [Mods.md](Mods.md) | 未实现 |
| 社区 | [Community.md](Community.md) | 未实现 |
| 设置 | [Settings.md](Settings.md) | 进行中：音乐音量和视距名会写入 user://settings.cfg。没有改键和渲染器重启 |
| 图鉴与帮助 | [Bestiary.md](Bestiary.md) | 未实现 |
| 本地多人 | [Multiplayer.md](Multiplayer.md) | 未实现 |
| VR | [Vr.md](Vr.md) | 不做。没有头显，也不接 OpenXR |
| 内容管线 | [Content.md](Content.md) | 进行中：已放入原版 Blocks.webp，按贴图槽取样。还没有界面图和模型 |
| 平台 | [Platforms.md](Platforms.md) | 未实现 |
