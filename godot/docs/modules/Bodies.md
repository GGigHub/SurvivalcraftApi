# 碰撞与移动

代码目录：玩家在 `scripts/Player/`，生物位移在 `scripts/Creatures/`，检测函数放在 `scripts/World/`，供两边调用。

原项目：`ComponentBody` 对格子做碰撞，不是对三角形网格。

## 必须

- 行走、下落、游泳、爬梯子、骑乘都对格子检测。
- 射线选中方块也读格子，命中结果包含方块 id 和面。
- 移动中的活塞体有单独占位，见 [MovingBlocks.md](MovingBlocks.md)。
- 玩家和生物共用同一套格子查询，禁止各写一个会漂移的碰撞函数。

## 禁止

- 用区块的 ConcavePolygonShape3D、HeightMapShape3D 或网格碰撞体。
- 用 CharacterBody3D 的默认 `move_and_slide` 去撞网格地形。CharacterBody3D 如果出现，只能当节点容器，位移必须来自格子检测。
- 把玩家做成 RigidBody3D。

## 完成标准

玩家能走到实心方块前停下，能跳上半砖，能穿过高一格的植物，射线能挖到看着的那一格。掉进水里会游泳而不是穿出世界。
