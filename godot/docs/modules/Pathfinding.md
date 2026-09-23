# 寻路

代码目录：`scripts/Creatures/`。

原项目：`SubsystemPathfinding` 与 `ComponentPathfinding`，在方块格子上搜索。

## 必须

- 寻路读取地形格子：能否站立、是否是水、是否是门。
- 搜索在后台或分帧进行，不能为了一只生物卡死主线程。
- 地形变化后，穿过该处的路径作废。

## 禁止

- NavigationServer3D、NavigationRegion3D、NavigationAgent3D。
- 把区块网格烘焙成导航网格。

## 完成标准

生物能绕过墙和坑，走向目标。玩家放下方块挡住路径后，生物会重算，而不是穿模走旧路径。
