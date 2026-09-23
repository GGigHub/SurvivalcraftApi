# 输入动作

代码目录：不单独建系统。动作名写在 `project.godot` 的 `[input]`，消费代码在 `scripts/Player/`。

原项目用自研输入，并能把四套键盘、四只鼠标拆开。本模块只锁定动作名字。设备限制见 [Multiplayer.md](Multiplayer.md)。

## 必须

动作名只用下面这些，全小写加下划线：

`move_forward`、`move_back`、`move_left`、`move_right`、`jump`、`sneak`、`attack`、`use`、`drop_item`、`open_inventory`、`pause`

默认键位可以后补。玩法代码只通过 `Input.IsActionPressed` 读这些名字。

## 禁止

- 另起 `Jump`、`jump`、`跳跃` 或其他别名。
- 在 UI 脚本和玩家脚本里各写一套按键判断。
- 用设备编号把多只鼠标分给多个玩家。Godot 做不到，不要写一套假设做得到的 API。

## 完成标准

游戏内移动、挖掘、放置、丢弃、打开背包、暂停都走上表。设置里的改键只改这些动作。
