# glTF 生物模组开发教程

本教程讲解如何在 Survivalcraft API 中使用 glTF/GLB 模型创建生物模组。支持两种方式：无需编写代码的纯数据驱动模式，以及需要编写 C# 组件的高级模式。

## 目录

1. [概述](#1-概述)
2. [前置要求](#2-前置要求)
3. [纯数据驱动：最小化生物模组](#3-纯数据驱动最小化生物模组)
4. [项目结构详解](#4-项目结构详解)
5. [数据库模板配置](#5-数据库模板配置)
6. [动画配置文件](#6-动画配置文件)
7. [添加自定义行为（C# 代码）](#7-添加自定义行为c-代码)
8. [构建与安装](#8-构建与安装)
9. [完整示例参考](#9-完整示例参考)

---

## 1. 概述

### glTF 模型的优势

与游戏原有的 `.dae`（Collada）模型相比，glTF 模型具有以下优势：

- **关键帧动画支持**：直接使用模型文件中自带的动画片段，无需 C# 代码手动驱动骨骼
- **无代码创建生物**：仅需配置文件即可完成一个完整生物模组
- **丰富的模型资源**：可利用大量公开的 glTF 模型资源（如 [glTF-Sample-Assets](https://github.com/KhronosGroup/glTF-Sample-Assets)）
- **标准化格式**：glTF 是 Khronos 制定的 3D 资源标准

### 支持的模型格式

| 格式 | 扩展名 | 说明 |
|------|--------|------|
| glTF | `.gltf` + `.bin` + 贴图文件 | 文本格式，外部资源引用 |
| GLB | `.glb` | 二进制格式，所有资源打包在单文件中 |
| Collada | `.dae` | 游戏原有格式，仍支持但建议使用 glTF |

---

## 2. 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Survivalcraft API 模组模板：[SurvivalcraftTemplateModForAPI](https://gitee.com/SC-SPM/SurvivalcraftTemplateModForAPI)
- 一个包含骨骼绑定的 glTF 或 GLB 模型文件（需带动画片段）

---

## 3. 纯数据驱动：最小化生物模组

这是最简方式——**无需编写任何 C# 代码**。只需准备模型文件、动画配置和数据库模板。

### 需要准备的文件

```
YourMod/
├── Assets/
│   ├── Animations/
│   │   └── YourCreature.json      # 动画配置
│   ├── Lang/
│   │   ├── en-US.json             # 英文本地化
│   │   └── zh-CN.json             # 中文本地化
│   ├── Models/
│   │   └── YourCreature/
│   │       ├── Creature.gltf      # glTF 模型
│   │       ├── Creature.bin       # 顶点/索引数据
│   │       └── Texture.png        # 贴图
│   └── YourDatabase.xdb           # 数据库模板
├── modinfo.json                   # 模组元数据
└── YourMod.csproj                 # 项目文件
```

### modinfo.json

```json
{
  "Name": "YourCreatureMod",
  "Version": "1.0.0",
  "ApiVersion": "1.9.2.1",
  "Description": "Adds a creature using glTF model",
  "ScVersion": "2.4",
  "GameplayImpactLevel": "Cosmetic",
  "PackageName": "yourname.YourCreatureMod",
  "Author": "YourName"
}
```

> `GameplayImpactLevel` 设为 `Cosmetic` 表示此模组仅添加外观内容，不影响游戏平衡。

### 本地化文件

`Assets/Lang/en-US.json`:
```json
{
  "DisplayName:YourCreature": "Your Creature",
  "Description:YourCreature": "A creature with glTF model"
}
```

`Assets/Lang/zh-CN.json`:
```json
{
  "DisplayName:YourCreature": "你的生物",
  "Description:YourCreature": "使用 glTF 模型的生物"
}
```

---

## 4. 项目结构详解

### 模型文件

将 glTF 模型文件放入 `Assets/Models/` 目录：

- **glTF 格式**：保持 `.gltf`、`.bin`、贴图文件的目录结构
- **GLB 格式**：单个 `.glb` 文件即可

游戏中通过 `ModelName` 参数引用，路径不含扩展名：

```xml
<!-- 引用 Assets/Models/YourCreature/Creature.gltf -->
<Parameter Name="ModelName" Value="Models/YourCreature/Creature" Type="string" />
```

### 模型中的动画片段

游戏会自动加载 glTF 文件中的所有动画片段。在动画配置文件中，通过动画片段名称引用它们。

查看模型中的动画片段名称：可以使用 [gltf-report](https://github.com/nicebyte/gltf-report) 或任何 glTF 查看器。

### csproj 配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultItems>false</EnableDefaultItems>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.1.3" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="modinfo.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <Target Name="PackageMod" AfterTargets="Build">
    <ZipDirectory SourceDirectory="$(OutputPath)"
                  DestinationFile="$(OutputPath)..\$(AssemblyName).scmod"
                  Overwrite="true" />
  </Target>
</Project>
```

---

## 5. 数据库模板配置

数据库模板（`.xdb` 文件）定义生物的属性和行为。关键是通过继承已有模板来减少配置量。

### 继承已有生物

最简方式是继承一个已有生物模板，仅覆盖需要修改的参数。以下是常用父模板：

| 父模板 | Guid | 适用场景 |
|--------|------|----------|
| Wolf（狼） | `e4275171-a39f-413f-8888-4c472868364d` | 四足地面生物 |
| LandAnimal（陆地动物） | `3f077159-f492-419b-859a-bb051de6339f` | 通用陆地生物 |
| Bird（鸟） | 参见游戏数据库 | 飞行生物 |

### 四足动物示例

继承 Wolf 模板，替换模型和动画配置，关闭旧的过程式动画：

```xml
<EntityTemplate Name="YourCreature"
               Guid="your-guid-here"
               InheritanceParent="e4275171-a39f-413f-8888-4c472868364d">

  <!-- 身体尺寸和质量 -->
  <MemberComponentTemplate Name="Body" Guid="...">
    <Parameter Name="BoxSize" Value="0.6,0.6,0.6" Type="Vector3" />
    <Parameter Name="Mass" Value="40" Type="float" />
  </MemberComponentTemplate>

  <!-- 移动参数 -->
  <MemberComponentTemplate Name="Locomotion" Guid="...">
    <Parameter Name="WalkSpeed" Value="5" Type="float" />
    <Parameter Name="TurnSpeed" Value="10" Type="float" />
  </MemberComponentTemplate>

  <!-- 关键：配置 glTF 模型和动画 -->
  <MemberComponentTemplate Name="FourLeggedModel" Guid="..."
                           InheritanceParent="76d24f74-5f25-4d30-92ef-658c1a4d66c8">
    <!-- 指向模型文件（不含扩展名） -->
    <Parameter Name="ModelName" Value="Models/YourCreature/Creature" Type="string" />

    <!-- 指向动画配置（不含 .json 扩展名） -->
    <Parameter Name="AnimationConfigPath" Value="Animations/YourCreature" Type="string" />

    <!-- 关闭旧过程式动画参数 -->
    <Parameter Name="WalkFrontLegsAngle" Value="0" Type="float" />
    <Parameter Name="WalkHindLegsAngle" Value="0" Type="float" />
    <Parameter Name="CanTrot" Value="False" Type="bool" />
  </MemberComponentTemplate>

  <!-- 生物分类 -->
  <MemberComponentTemplate Name="Creature" Guid="...">
    <Parameter Name="Category" Value="LandPredator" Type="Game.CreatureCategory" />
    <Parameter Name="DisplayName" Value="[DisplayName:YourCreature]" Type="string" />
    <Parameter Name="Description" Value="[Description:YourCreature]" Type="string" />
  </MemberComponentTemplate>

  <!-- 刷怪蛋 -->
  <ParameterSet Name="CreatureEggData" Guid="...">
    <Parameter Name="TextureSlot" Value="15" Type="int" />
    <Parameter Name="Color" Value="204,102,0" Type="Color" />
    <Parameter Name="EggTypeIndex" Value="682" Type="int" />
  </ParameterSet>
</EntityTemplate>
```

### 关键配置说明

- **`InheritanceParent`**：继承已有生物模板，自动获得其所有组件和行为
- **`FourLeggedModel`** 的 `InheritanceParent`：必须继承 `76d24f74-5f25-4d30-92ef-658c1a4d66c8`（FourLeggedModel 基础模板）
- **关闭旧动画参数**：将 `WalkFrontLegsAngle` 和 `WalkHindLegsAngle` 设为 `0`，`CanTrot` 设为 `False`，确保不与 glTF 关键帧动画冲突

---

## 6. 动画配置文件

动画配置文件（`.json`）是 glTF 生物模组的核心，定义了动画如何播放和切换。

### 最小化配置

以下是一个使用 FourLegged 内置模板的最小配置：

```json
{
  "template": "FourLegged",
  "rootBoneRotation": 180,
  "modelScale": 0.01,

  "animations": {
    "idle": {
      "source": "Survey",
      "speed": 1.0,
      "loop": true,
      "blendDuration": 0.3
    },
    "walk": {
      "source": "Walk",
      "speed": "[SpeedAbs] * 0.65",
      "loop": true,
      "blendDuration": 0.2
    },
    "run": {
      "source": "Run",
      "speed": "[SpeedAbs] * 0.5",
      "loop": true,
      "blendDuration": 0.15
    }
  },

  "states": {
    "gait": {
      "layer": "Base",
      "rules": [
        {
          "condition": "IsDead",
          "animation": null
        },
        {
          "condition": "[SpeedAbs] > 0.7 * [WalkSpeed]",
          "animation": { "source": "run" }
        },
        {
          "condition": "[SpeedAbs] > 0.3",
          "animation": { "source": "walk" }
        },
        {
          "condition": "true",
          "animation": { "source": "idle" }
        }
      ]
    },
    "death": {
      "layer": "Death",
      "rules": [
        {
          "condition": "[DeathPhase] > 0",
          "animation": { "source": "driver:Death" }
        },
        {
          "condition": "true",
          "animation": null
        }
      ]
    }
  }
}
```

### 顶层属性说明

| 属性 | 类型 | 说明 |
|------|------|------|
| `template` | string | 动画模板名称：`Simple`、`FourLegged`、`Human`、`Bird`、`FlightlessBird`、`Fish`，或自定义模板路径 |
| `rootBoneRotation` | float | 根骨骼旋转修正（度），用于修正模型朝向。常见值：`0`（模型面向 +Z）或 `180`（模型面向 -Z） |
| `modelScale` | float | 模型缩放。`1.0` 为原始大小，`0.01` 用于厘米单位的模型 |
| `layers` | object | 层级覆盖配置（可选，覆盖模板默认值） |
| `animations` | object | 动画别名定义 |
| `states` | object | 状态规则 |
| `parameters` | object | 初始参数值 |

### 选择模板

| 模板 | 适用生物 | 预设层级 | 预设状态轨道 |
|------|----------|----------|-------------|
| `Simple` | 简单实体 | Base | Gait (Idle) |
| `FourLegged` | 四足动物 | Base, Head, Death | Gait, Activity, Death |
| `Human` | 人形 | Base, Activity, Ride, Death | Locomotion, Activity, Ride, Death |
| `Bird` | 鸟类 | Base, Head, Death | Locomotion, Activity, Death |
| `FlightlessBird` | 不会飞的鸟 | Base, Head, Death | Locomotion, Activity, Death |
| `Fish` | 鱼类 | Base, Head, Death | Swim, Activity, Death |

详细的模板内容和 JSON 格式参见 [AnimationConfigReference.md](AnimationConfigReference.md)。

### speed 表达式

动画的 `speed` 属性支持表达式，使用 `[参数名]` 引用运行时参数：

```json
"speed": "[SpeedAbs] * 0.65"
```

常用参数：
- `[SpeedAbs]`：生物当前移动速度的绝对值
- `[WalkSpeed]`：生物的行走速度设定值
- `[DeathPhase]`：死亡阶段（0-1）

更多表达式用法参见 [AnimationConfigReference.md](AnimationConfigReference.md)。

---

## 7. 添加自定义行为（C# 代码）

当纯数据驱动不够灵活时，可以编写 C# 组件实现自定义行为。这需要：

1. 创建自定义的 `ComponentCreatureModel` 子类来同步动画参数
2. 创建行为组件来实现复杂的游戏逻辑
3. 在数据库模板中注册自定义组件

### 7.1 项目配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.1.3" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="Components\*.cs" />
    <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="modinfo.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <Target Name="PackageMod" AfterTargets="Build">
    <ZipDirectory SourceDirectory="$(OutputPath)"
                  DestinationFile="$(OutputPath)..\$(AssemblyName).scmod"
                  Overwrite="true" />
  </Target>
</Project>
```

### 7.2 自定义模型组件

自定义模型组件负责同步游戏状态到 AnimationController 的参数：

```csharp
using Engine.Animation;

namespace Game;

public class ComponentYourCreatureModel : ComponentCreatureModel
{
    public override void SyncAnimationParameters()
    {
        // 必须调用基类方法——它同步了大量内置参数
        // 包括：Speed, SpeedAbs, DeathPhase, IsDead, WalkSpeed, LookAngleX/Y 等
        base.SyncAnimationParameters();

        var controller = AnimationController;
        if (controller == null) return;

        // 添加模组自定义参数（基类不提供的）
        // 通过 Parameters.SetString 设置状态轨道值，触发状态规则评估
        var locomotion = m_componentCreature.ComponentLocomotion;
        float speed = Vector3.Dot(
            m_componentCreature.ComponentBody.Velocity,
            m_componentCreature.ComponentBody.Forward
        );
        float walkSpeed = locomotion?.WalkSpeed ?? 5f;
        string gait = speed > 0.7f * walkSpeed ? "Run" :
                      speed > 0.2f ? "Walk" : "Idle";
        controller.Parameters.SetString("Gait", gait);
    }
}
```

**`SyncAnimationParameters()`** 是关键方法。每帧调用，负责将游戏状态写入 AnimationController 的参数系统。

> **重要**：必须调用 `base.SyncAnimationParameters()`。基类同步了大量内置参数（Speed, SpeedAbs, DeathPhase, IsDead, WalkSpeed, LookAngleX/Y, BodyHeight 等），不调用会导致基础功能异常。

设置状态轨道值使用 `Parameters.SetString("Gait", "Run")` 或 `Parameters.SetBool("Death", true)`，而非 `SetState()`。参数变化通过状态规则的条件表达式间接驱动动画切换。

### 7.3 手动动画控制

对于攻击等特殊动作，可以使用手动动画控制 API：

```csharp
// 强制播放攻击动画
AnimationController.PlayAnimation("UpperBodyAction", "bite", loop: true);

// 攻击结束后释放手动控制，回到状态规则驱动
AnimationController.ReleaseManualControl("UpperBodyAction");

// 检查某个层是否处于手动控制状态
bool isManual = AnimationController.IsManualControl("UpperBodyAction");
```

### 7.4 动画事件处理

在动画配置中定义事件，在 C# 中处理：

```json
"bite": {
  "source": "Bite",
  "loop": true,
  "events": [
    { "time": 0.577, "name": "AttackHit" }
  ]
}
```

```csharp
public class ComponentYourCreatureModel : ComponentCreatureModel
{
    // 基类 ComponentCreatureModel.SetModel() 已自动订阅
    // AnimationController.OnAnimationEvent 到 HandleAnimationEvent。
    // 只需 override 此方法即可处理事件。

    public override void HandleAnimationEvent(AnimationEvent animationEvent)
    {
        // 先调用基类处理内置事件（Footstep、AttackHit 等）
        base.HandleAnimationEvent(animationEvent);

        switch (animationEvent.Name)
        {
            case "MyCustomEvent":
                // 处理自定义事件
                DoSomething();
                break;
        }
    }
}
```

> `ComponentCreatureModel.SetModel()` 已自动将 `AnimationController.OnAnimationEvent` 订阅到 `HandleAnimationEvent`。子类只需 override `HandleAnimationEvent` 即可，不要手动订阅。基类已处理 `AttackHit`、`Footstep`、`AttackStart`、`AttackEnd` 等内置事件。

### 7.5 在数据库中注册自定义组件

```xml
<!-- 定义组件模板 -->
<ComponentTemplate Name="YourCreatureModel"
                   Description="Custom creature model"
                   Guid="your-guid"
                   InheritanceParent="681a5886-5bff-418a-bf5f-ac84f290a311">
  <Parameter Name="Class" Value="Game.ComponentYourCreatureModel" Type="string" />
  <Parameter Name="ModelName" Value="" Type="string" />
  <Parameter Name="TextureOverride" Value="" Type="string" />
  <Parameter Name="AnimationConfigPath" Value="" Type="string" />
</ComponentTemplate>

<!-- 在实体中使用 -->
<MemberComponentTemplate Name="YourCreatureModel" Guid="..."
                         InheritanceParent="your-component-template-guid">
  <Parameter Name="ModelName" Value="Models/YourCreature/Model" Type="string" />
  <Parameter Name="AnimationConfigPath" Value="Animations/YourCreature" Type="string" />
</MemberComponentTemplate>
```

> `InheritanceParent="681a5886-5bff-418a-bf5f-ac84f290a311"` 指向 ComponentCreatureModel 基础模板。

---

## 8. 构建与安装

### 构建

```bash
dotnet build
```

构建成功后，`.scmod` 文件会生成在 `bin/Debug/` 目录。

### 安装

将 `.scmod` 文件复制到游戏的 Mods 目录：

- **Windows**：`Survivalcraft.Windows/bin/Debug/Mods/`（开发环境）
- 或游戏安装目录下的 `Mods/` 文件夹

### 测试

1. 启动游戏
2. 使用对应生物的刷怪蛋生成实体
3. 观察模型加载和动画播放

---

## 9. 完整示例参考

本教程的内容基于两个完整示例模组：

| 模组 | 说明 |
|------|------|
| **BaseGltfFoxMod** | 无代码纯数据驱动的四足生物。使用 FourLegged 模板，包含 Survey/Walk/Run 三个动画。 |
| **AdvancedGltfFoxMod** | 高级四足生物。包含自定义模型组件、IK 头部追踪、动画驱动的跳跃、嚎叫/坐下行为、攻击动画、Root Motion 跳跃等。 |

更多 API 细节参见：
- [AnimationConfigReference.md](AnimationConfigReference.md) — 动画配置 JSON 完整参考
- [AnimationAdvancedTopics.md](AnimationAdvancedTopics.md) — IK、Root Motion、表达式等高级主题
- [CreatureModelSystem.md](CreatureModelSystem.md) — 模型系统整体架构
