# 代码目录

新代码只能放在下表的目录。目录不存在就按表创建。禁止在 `scenes/boot/` 里写玩法，禁止把区块网格保存成场景。

| 目录 | 只放什么 |
|------|----------|
| `scripts/App/` | 启动、读档入口、设置、社区、公告 |
| `scripts/World/` | 区块存储、光照、生成、流体、天空规则 |
| `scripts/Blocks/` | 方块定义、形状、挖掘放置 |
| `scripts/Simulation/` | 电路、活塞、爆炸、投射物、粒子规则、时间与季节 |
| `scripts/Creatures/` | 生物数据、行为、生成、格子寻路 |
| `scripts/Items/` | 物品、配方、背包、熔炉、箱子、发射器、衣物数据 |
| `scripts/Player/` | 玩家移动、生存属性、输入消费 |
| `scripts/Mods/` | 模组扫描、数据合并、回调分发 |
| `scripts/Vr/` | 不再使用。不做 VR |
| `scenes/ui/` | 只放 Control 场景 |
| `scenes/creatures/` | 只放骨骼、网格和动画，不放 AI |
| `scenes/vr/` | 只放 XR 原点与控制器节点 |
| `assets/` | 随安装包发布、走编辑器导入的资源 |

渲染网格由 `scripts/World/` 在运行时创建，不放进 `scenes/`。

输入动作名集中在 [modules/Input.md](modules/Input.md)。禁止在各个脚本里各写一套字符串。

旧引擎代码只作规则参考，路径在仓库根目录，不在本工程内。
