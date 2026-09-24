# 时间与环境

代码目录：规则在 `scripts/World/` 与 `scripts/Simulation/`。天空外观用 Godot 环境，数值来自 C#。

原项目：`SubsystemTime`、`SubsystemTimeOfDay`、`SubsystemSeasons`、`SubsystemWeather`、`SubsystemSky`。

## 必须

- 有世界时间。昼夜循环影响环境光，并通知 [Lighting.md](Lighting.md) 更新顶点色。
- 季节影响温度和降水类型，温度再进入 [Survival.md](Survival.md)。季节按世界时钟每 4 天一换：春、夏、秋、冬。天气由当天时刻决定：春和秋下雨，夏天打雷，冬天下雪，其余为晴。雨和雷让人变湿，雪只在冬天的下雪时段出现。
- 天气包含晴、雨、雪、雷暴中原游戏有的种类。闪电可以点燃或伤害，规则与原游戏一致。
- 天空、太阳、月亮、雾是渲染。雾的距离可以跟视距设置走，但不能拿雾替代区块卸载。

## 禁止

- 只用 WorldEnvironment 的过程天空，却没有 C# 时间、季节和天气状态。
- 把雨雪做成永不停止的粒子，和天气状态脱节。

## 完成标准

一天里亮度会变化。季节会变。雨和雪只在对应天气出现。睡觉能推进时间，见生存模块。
