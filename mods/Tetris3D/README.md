# Tetris3D 模组

生存战争插件版世界内《俄罗斯方块》游戏机。文档见仓库 [`docs/mods/Tetris3D/`](../../docs/mods/Tetris3D/)。

## 构建

在本目录或仓库根目录执行：

```bat
dotnet build mods/Tetris3D/Tetris3D.csproj
```

成功后生成 `mods/Tetris3D/bin/Tetris3D.scmod`，并尽量复制到 `Survivalcraft.Windows/bin/Debug/Mods/`。

## 本地测试

1. 构建本模组
2. 确认 `.scmod` 已在游戏 `Mods/` 目录
3. 启动 `Survivalcraft.Windows` 并**进入一个世界**
4. 创造模式物品栏「杂物」里找「俄罗斯方块机」，放下后走到正面交互
5. 生存模式可用 8 木板 + 中间 1 玻璃合成

主菜单右下角仍保留全屏调试入口。

## 操作

| 动作 | 键盘 | 触屏（仅菜单 Screen） |
|------|------|------|
| 左右 | ← → / A D | ◀ ▶ |
| 软降 | ↓ / S | ▼ |
| 硬降 | Space | ⇩ |
| 顺/逆时针 | ↑ X / Z | ↻ ↺ |
| Hold | C / Shift | H |
| 暂停 | P | 顶栏按钮 |
| 离开机台 | 再交互一次，或走开 | — |

## 发布

只分发 `Tetris3D.scmod`，不要打包游戏本体。适配 API `1.9.3.1`。
