# 动画系统高级主题

本文档涵盖动画系统的高级 API 用法，适合需要在 C# 代码中深度定制动画行为的模组开发者。

## 目录

1. [AnimationController API](#1-animationcontroller-api)
2. [参数系统](#2-参数系统)
3. [手动动画控制](#3-手动动画控制)
4. [动画事件处理](#4-动画事件处理)
5. [IK 系统](#5-ik-系统)
6. [Root Motion](#6-root-motion)
7. [表达式系统](#7-表达式系统)
8. [动画镜像](#8-动画镜像)
9. [Blend Spaces](#9-blend-spaces)
10. [自定义驱动器](#10-自定义驱动器)

---

## 1. AnimationController API

`AnimationController` 是动画系统的核心控制器。它在 `ComponentModel.SetModel()` 时自动创建，通过 `AnimationConfigPath` 或 `AnimationTemplateName` 参数触发。

### 访问 Controller

在自定义 `ComponentCreatureModel` 子类中通过属性访问：

```csharp
var controller = AnimationController;
if (controller != null)
{
    // 使用 API ...
}
```

### 核心 API 一览

| API | 说明 |
|-----|------|
| `Update(float deltaTime)` | 每帧更新（由系统自动调用） |
| `ComputeBoneTransforms(Matrix?[] boneTransforms)` | 计算骨骼变换（由系统自动调用） |
| `SetState(string trackName, object value)` | 设置状态轨道值 |
| `GetState(string trackName)` | 获取状态轨道值 |
| `PlayAnimation(...)` | 手动播放动画 |
| `ReleaseManualControl(...)` | 释放手动控制 |
| `RegisterAndBuildIKChain(...)` | 注册 IK 链 |
| `SetIKAim(...)`, `SetIKTarget(...)` | 设置 IK 目标 |
| `SetRootMotionConfig(RootMotionConfig)` | 设置 Root Motion 配置 |
| `SetDriver(string layerName, IAnimationDriver driver)` | 设置层级驱动器 |

### 生命周期

```
每帧：
  1. SyncAnimationParameters()  → 开发者同步参数
  2. AnimationController.Update(deltaTime)
     - 评估状态规则
     - 更新各层动画
     - 应用 Root Motion
     - 检查完成事件
  3. AnimationController.ComputeBoneTransforms(boneTransforms)
     - 层混合
     - IK 求解
     - Morph Target
```

### 关键属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Model` | `Model` | 目标模型 |
| `Template` | `AnimationTemplate` | 使用的模板 |
| `Parameters` | `AnimationParameters` | 参数容器 |
| `Layers` | `AnimationLayer[]` | 层级数组 |
| `ExpressionEvaluator` | `ExpressionEvaluator` | 表达式求值器 |
| `IKSolver` | `IKSolver` | IK 求解器（延迟初始化） |
| `RootBoneRotation` | float | 根骨骼旋转修正（弧度） |
| `ModelScale` | float | 模型缩放 |
| `Velocity` | Vector3? | 速度向量（Root Motion 修改此值） |
| `HasRootMotion` | bool | 是否有 Root Motion 配置 |

---

## 2. 参数系统

`AnimationParameters` 是类型化的参数存储，支持脏标记追踪。参数是表达式和状态规则的数据来源。

### 设置参数

```csharp
var p = controller.Parameters;

p.SetFloat("SpeedAbs", speedValue);
p.SetBool("Death", isDead);
p.SetVector3("LookDirection", direction);
p.SetString("GaitState", "Run");

// 通用设置（自动分发类型）
p.SetParameter("CustomValue", 3.14f);
```

### 读取参数

```csharp
float speed = p.GetFloat("SpeedAbs");       // 不存在时返回 0
bool dead = p.GetBool("Death");             // 不存在时返回 false
Vector3 dir = p.GetVector3("LookDirection"); // 不存在时返回 Zero

// 安全读取
if (p.TryGetFloat("SpeedAbs", out float speed)) { ... }
```

### 脏标记

```csharp
p.IsDirty;      // 参数是否在本帧被修改
p.ClearDirty();  // 清除脏标记
p.SetDirty();    // 强制标记为脏
```

### 常用参数名

| 参数名 | 类型 | 说明 | 来源 |
|--------|------|------|------|
| `SpeedAbs` | float | 移动速度绝对值 | `SyncAnimationParameters()` |
| `WalkSpeed` | float | 行走速度设定 | 配置文件 parameters |
| `DeathPhase` | float | 死亡阶段 0-1 | `SyncAnimationParameters()` |
| `LookAngleX` | float | 俯仰角（度） | `SyncAnimationParameters()` |
| `LookAngleY` | float | 偏航角（度） | `SyncAnimationParameters()` |
| `BodyHeight` | float | 身体高度 | `SyncAnimationParameters()` |
| `BodyRight` | float | 身体右侧方向 | `SyncAnimationParameters()` |

参数名没有硬编码限制——配置文件和 C# 代码使用相同的参数名即可通信。

---

## 3. 手动动画控制

手动控制允许临时脱离状态规则，直接指定播放的动画。适合攻击、特殊技能等需要精确控制的场景。

### PlayAnimation

强制在指定层级播放动画，层级进入手动控制状态，跳过状态规则评估：

```csharp
// 在 UpperBodyAction 层播放 bite 动画
bool success = controller.PlayAnimation(
    layerName: "UpperBodyAction",
    animationNameOrAlias: "bite",
    loop: true,
    blendDuration: 0.3f
);

// 播放外部文件中的动画
controller.PlayExternalAnimation(
    layerName: "Base",
    filePath: "Models/Extra/SpecialAnims.glb",
    animationName: "Dance",
    loop: false,
    blendDuration: 0.5f
);
```

`animationNameOrAlias` 可以是：
- 配置文件 `animations` 中定义的别名（如 `"bite"`）
- 模型文件中的动画片段名称

### ReleaseManualControl

释放手动控制，恢复状态规则驱动：

```csharp
// 释放指定层级
controller.ReleaseManualControl("UpperBodyAction");

// 释放所有层级
controller.ReleaseManualControl();
```

### IsManualControl

检查层级是否处于手动控制：

```csharp
if (controller.IsManualControl("UpperBodyAction"))
{
    // 正在手动控制中
}
```

### 典型模式：攻击动画

```csharp
// 开始攻击
public void StartAttack()
{
    controller.PlayAnimation("UpperBodyAction", "bite", loop: true);
}

// 在动画事件回调中检测攻击命中
private void HandleEvent(AnimationEvent evt)
{
    if (evt.Name == "AttackHit")
    {
        PerformDamage();
    }
}

// 攻击结束
public void EndAttack()
{
    controller.ReleaseManualControl("UpperBodyAction");
}
```

### StopAnimation

停止层级播放但不释放手动控制：

```csharp
controller.StopAnimation("UpperBodyAction");
// 层级停止播放但保持手动控制状态
// 需要调用 ReleaseManualControl 才会恢复状态规则
```

---

## 4. 动画事件处理

### 订阅事件

在模型组件中订阅 AnimationController 的事件：

```csharp
public override void OnModelLoaded()
{
    base.OnModelLoaded();
    if (AnimationController != null)
    {
        AnimationController.OnAnimationEvent += OnAnimationEvent;
    }
}

private void OnAnimationEvent(AnimationEvent evt)
{
    switch (evt.Name)
    {
        case "AttackHit":
            // 在动画指定时间点执行攻击判定
            PerformAttack();
            break;
        case "PlaySound":
            // 播放音效
            SubsystemAudio.PlaySound((string)evt.Parameter, ...);
            break;
        case "JumpLandRecoveryComplete":
            // 跳跃恢复结束
            m_jumpState = JumpState.None;
            break;
    }
}
```

### AnimationEvent 结构

| 字段 | 类型 | 说明 |
|------|------|------|
| `Name` | string | 事件名称（配置中定义） |
| `Time` | float | 触发时的归一化时间 0-1 |
| `Parameter` | object | 附加数据（配置中的 `data` 字段） |

### OnComplete 自动动作

非循环动画完成时可自动触发状态变更，无需 C# 代码：

```json
{
  "source": "Bark",
  "loop": false,
  "onComplete": {
    "type": "setState",
    "state": "Activity",
    "value": "None"
  }
}
```

在 C# 中监听：

```csharp
// onComplete type="trigger" 会通过 OnAnimationEvent 触发
private void OnAnimationEvent(AnimationEvent evt)
{
    if (evt.Name == "BarkComplete") { ... }
}
```

---

## 5. IK 系统

IK（Inverse Kinematics，逆向运动学）允许通过指定目标位置/方向来反向计算骨骼的关节角度。常用于头部追踪、手部抓取等。

### 概述

IK 作为动画混合之后的后处理步骤运行。流程：

```
动画混合 → IK 求解 → 最终骨骼变换
```

### 注册 IK 链

在模型加载后注册 IK 链：

```csharp
public override void OnModelLoaded()
{
    base.OnModelLoaded();
    if (AnimationController != null)
    {
        // 注册从根到头部的 IK 链，使用 SingleBoneIK 算法
        AnimationController.RegisterAndBuildIKChain(
            name: "Head",
            endBoneName: "b_Head_05",
            algorithmName: "SingleBoneIK",
            maxChainLength: 3
        );
    }
}
```

参数说明：
- `name`：IK 链名称，后续通过此名称设置目标
- `endBoneName`：末端骨骼名称。系统自动从该骨骼向上回溯构建链
- `algorithmName`：算法名称，null 使用默认。可选：`SingleBoneIK`、`TwoBoneIK`、`CCD`、`FABRIK`
- `maxChainLength`：最大链长度（骨骼数），默认 3

### 设置 IK 目标

每帧更新 IK 目标：

```csharp
public override void SyncAnimationParameters()
{
    base.SyncAnimationParameters();

    var controller = AnimationController;
    if (controller == null) return;

    // 方向约束：让头部朝向目标方向
    Vector3 aimDirection = CalculateAimDirection();
    float weight = CalculateIKWeight(); // 0-1，通常随距离衰减
    controller.SetIKAim("Head", aimDirection, weight);

    // 位置约束：让骨骼到达目标位置
    // controller.SetIKTarget("Hand", targetPosition, weight);
}
```

### 清除 IK 目标

```csharp
controller.ClearIKTarget("Head");
```

### IK 目标类型

| 方法 | 说明 |
|------|------|
| `SetIKAim(name, direction, weight)` | 方向约束——让末端骨骼朝向指定方向 |
| `SetIKTarget(name, position, weight)` | 位置约束——让末端骨骼到达指定位置 |
| `SetIKTarget(name, IKTarget)` | 完整目标（位置 + 方向 + 提示方向） |

### IKTarget 详细字段

```csharp
var target = IKTarget.CombinedTarget(
    position: targetPos,       // 目标位置
    aimDirection: aimDir,      // 目标方向
    positionWeight: 1.0f,      // 位置权重
    aimWeight: 0.8f            // 方向权重
);
target.Hint = elbowHintDir;    // 弯曲提示方向（如肘部方向）
target.PositionSmoothTime = 0.1f;  // 位置平滑时间
target.AimSmoothTime = 0.15f;      // 方向平滑时间
controller.SetIKTarget("Hand", target);
```

### IK 算法

| 算法 | 链长 | 支持 Aim | 说明 |
|------|------|---------|------|
| **SingleBoneIK** | 1-2 | 是 | 单骨骼朝向旋转。适合颈部-头部 |
| **TwoBoneIK** | 3 | 是 | 解析解（余弦定理），精确高效。适合上臂-前臂-手 |
| **CCD** | 任意 | 否 | 循环坐标下降，迭代求解。适合多关节链 |
| **FABRIK** | 任意 | 否 | 前后到达 IK，收敛性好。适合长链 |

### 关节限制

为链中的骨骼添加旋转限制：

```csharp
IKChain chain = controller.GetIKChain("Head");
chain.SetJointLimit("Neck", JointLimit.FromDegrees(
    minDeg: new Vector3(-40, -30, -20),
    maxDeg: new Vector3(40, 30, 20),
    EulerRotationOrder.YXZ
));
```

### 不可达策略

当目标超出 IK 链可达范围时的行为：

```csharp
chain.UnreachableStrategy = UnreachableStrategy.ExtendTowardTarget;
```

| 策略 | 说明 |
|------|------|
| `ExtendTowardTarget` | 完全伸展朝向目标（默认） |
| `KeepCurrentPose` | 保持当前姿态不变 |
| `UseLastValidResult` | 使用上一次有效结果 |

### 完整示例：头部追踪

```csharp
public class ComponentFoxStareBehavior : ComponentStareBehavior
{
    private const string HeadIKChain = "Head";

    public override void OnModelLoaded()
    {
        base.OnModelLoaded();
        var controller = ComponentCreatureModel?.AnimationController;
        if (controller != null)
        {
            controller.RegisterAndBuildIKChain(
                HeadIKChain,
                "b_Head_05",
                "SingleBoneIK",
                maxChainLength: 3
            );
        }
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        var controller = ComponentCreatureModel?.AnimationController;
        if (controller == null || m_stareTarget == null) return;

        // 计算朝向目标的方向
        Vector3 modelDir = WorldToModelDirection(m_stareTarget.Value);

        // 权重随距离衰减
        float dist = Vector3.Distance(Position, m_stareTarget.Value);
        float weight = MathHelper.Clamp(1.0f - (dist - MinRange) / (MaxRange - MinRange), 0.3f, 1.0f);

        controller.SetIKAim(HeadIKChain, modelDir, weight);
    }
}
```

---

## 6. Root Motion

Root Motion 允许动画中的根骨骼运动驱动生物的实际物理移动。

### 模式

| 模式 | 说明 | 适用场景 |
|------|------|----------|
| `None` | 不使用（默认） | — |
| `Blend` | 将动画位移混合到速度 | 平滑移动过渡 |
| `AddImpulse` | 在指定相位施加脉冲速度 | 跳跃起飞 |
| `Override` | 动画位移覆盖位置 | 精确路径移动 |

### C# 中使用 Root Motion

Controller 需要关联速度向量，Root Motion 才能生效：

```csharp
// 在 OnModelLoaded 中关联
controller.Velocity = ref componentBody.Velocity;
controller.EntityRotation = componentBody.Rotation;
controller.SetCollisionBox = (size) => componentBody.BoxSize = size;
controller.DefaultCollisionSize = originalBoxSize;
```

### AddImpulse 模式详解

AddImpulse 模式在动画的指定相位点提取速度并作为脉冲施加：

1. 动画播放到 `impulsePhase` 时
2. 系统计算该点附近的峰值/平均速度
3. 速度乘以 `impulseScale` 并通过 `velocityMask` 过滤
4. 结果脉冲施加到生物的速度向量

```json
"jump": {
  "source": "Jump",
  "loop": false,
  "rootMotion": {
    "sourceBone": "Hips",
    "translation": {
      "mode": "AddImpulse",
      "impulsePhase": 0.182,
      "impulseMethod": "Peak",
      "velocityMask": [0, 1, 0],
      "impulseScale": 2.1,
      "maxImpulse": 15
    }
  }
}
```

- `impulsePhase: 0.182` — 跳跃动画的起跳点
- `velocityMask: [0, 1, 0]` — 仅影响 Y 轴（垂直方向）
- `impulseMethod: "Peak"` — 使用峰值速度

### RootMotionCache API

可以在 C# 中查询动画的运动数据：

```csharp
// 获取整个动画的平均速度
Vector3 avgVel = cache.GetAverageVelocity();

// 获取指定相位范围的速度
Vector3 rangeVel = cache.GetAverageVelocity(0.0f, 0.3f);

// 获取峰值速度
Vector3 peakVel = cache.GetPeakVelocity();

// 获取总位移
Vector3 totalDisp = cache.GetTotalTranslation();
```

### 动态切换 Root Motion

可以在运行时切换 Root Motion 配置：

```csharp
// 启用 Blend 模式
controller.SetRootMotionConfig(new RootMotionConfig
{
    SourceBone = "Hips",
    Translation = new TranslationConfig
    {
        Mode = TranslationMode.Blend,
        BlendMethod = BlendMethod.SmoothDamp,
        SmoothTime = 0.3f
    }
});

// 禁用
controller.SetRootMotionConfig(null);
```

---

## 7. 表达式系统

表达式系统基于 NCalc，用于在配置文件中编写动态逻辑。

### 表达式检测

以下格式的字符串会被识别为表达式：
- 包含 `[参数名]` 的字符串
- 以 `expr:` 前缀开头的字符串

```json
"speed": "[SpeedAbs] * 0.65"        // 含参数引用 → 表达式
"speed": "expr:1.0 + sin(pi)"       // expr: 前缀 → 表达式
"speed": 1.0                        // 纯数值 → 静态值
```

### 动画系统自定义函数

以下函数由 AnimationExpressionFunctions 注册：

| 函数/常量 | 说明 | 示例 |
|-----------|------|------|
| `lerp(a, b, t)` | 线性插值 a + (b-a)*t | `lerp(0, 1, 0.5)` = 0.5 |
| `smoothstep(t)` | 平滑阶梯 t*t*(3-2t) | `smoothstep(0.5)` = 0.5 |
| `clamp(v, min, max)` | 钳制 | `clamp([x], 0, 1)` |
| `degtorad(v)` | 度 → 弧度 | `degtorad(90)` ≈ 1.571 |
| `radtodeg(v)` | 弧度 → 度 | `radtodeg(3.14)` ≈ 179.9 |
| `pi` | 圆周率（零参数函数调用） | `pi` ≈ 3.14159 |

### NCalc 内置函数

NCalc 表达式引擎本身提供了以下常用数学函数，无需额外注册即可使用：

| 函数 | 说明 |
|------|------|
| `abs(v)` | 绝对值 |
| `min(a, b)` / `max(a, b)` | 最小/最大值 |
| `sin(v)` / `cos(v)` | 正弦/余弦 |
| `sqrt(v)` | 平方根 |

### DynamicProperty

在 C# 中可以使用 `DynamicProperty<T>` 处理可能是静态值或表达式的配置：

```csharp
var prop = DynamicProperty<float>.FromExpression("[SpeedAbs] * 0.5");
float value = prop.GetValue(controller.Parameters, controller.ExpressionEvaluator);
```

---

## 8. 动画镜像

动画镜像允许复用一侧的动画到另一侧（如左手动画镜像为右手动画），通过骨骼重映射实现。

### 配置

```csharp
// 在 ClipAnimationSource 上设置骨骼重映射
var boneRemapping = new Dictionary<string, string>
{
    { "Hand_L", "Hand_R" },
    { "Arm_L", "Arm_R" },
    { "Leg_L", "Leg_R" }
};
```

镜像功能在 ClipAnimationSource 层面设置，通过代码而非 JSON 配置使用。

---

## 9. Blend Spaces

Blend Space 允许根据参数值平滑混合多个动画。

### 1D Blend Space

根据一个参数值在多个动画间混合：

```csharp
// 编程方式创建
var blendSpace = new AnimationBlendSpaceDefinition
{
    Name = "Movement",
    ParameterName = "SpeedNorm",
    SyncTime = true,
    Samples = new[]
    {
        new AnimationBlendSample { Value = 0.0f, AnimationName = "Idle" },
        new AnimationBlendSample { Value = 0.5f, AnimationName = "Walk" },
        new AnimationBlendSample { Value = 1.0f, AnimationName = "Run" }
    }
};
```

当 `SpeedNorm` = 0 时播放 Idle，= 0.5 时播放 Walk，= 1.0 时播放 Run，中间值自动混合。

`SyncTime = true`（默认）使所有动画同步时间进度，避免混合时出现跳帧。

### 2D Blend Space

根据两个参数值混合：

```csharp
var blendSpace2D = new AnimationBlendSpaceDefinition2D
{
    ParameterNameX = "MoveX",
    ParameterNameY = "MoveY",
    Samples = new[]
    {
        new AnimationBlendSample2D { ValueX = 0, ValueY = 0, AnimationName = "Idle" },
        new AnimationBlendSample2D { ValueX = 1, ValueY = 0, AnimationName = "WalkFwd" },
        new AnimationBlendSample2D { ValueX = -1, ValueY = 0, AnimationName = "WalkBack" },
        new AnimationBlendSample2D { ValueX = 0, ValueY = 1, AnimationName = "WalkLeft" },
        new AnimationBlendSample2D { ValueX = 0, ValueY = -1, AnimationName = "WalkRight" }
    }
};
```

2D Blend Space 使用反距离加权混合最近的 4 个采样点。

---

## 10. 自定义驱动器

可以实现 `IAnimationDriver` 接口创建自定义驱动器。

> **提醒**：驱动器主要是为游戏本体 `.dae` 模型的 C# 硬编码动画设计的兼容机制。glTF 模型优先使用关键帧动画。仅在有特殊算法需求时才建议自定义驱动器。

### IAnimationDriver 接口

```csharp
public interface IAnimationDriver
{
    string Name { get; }
    AnimationBlendMode BlendMode { get; }
    string[] TargetBones { get; }
    void Update(float deltaTime, AnimationParameters parameters);
    void SampleTransforms(Matrix?[] boneTransforms, Model model);
}
```

### 实现示例

```csharp
public class CustomHeadBobDriver : IAnimationDriver
{
    public string Name => "CustomHeadBob";
    public AnimationBlendMode BlendMode => AnimationBlendMode.Override;
    public string[] TargetBones => ["Head"];

    private float m_time;

    public void Update(float deltaTime, AnimationParameters parameters)
    {
        m_time += deltaTime;
    }

    public void SampleTransforms(Matrix?[] boneTransforms, Model model)
    {
        ModelBone head = model.FindBone("Head");
        if (head == null) return;

        float bob = MathF.Sin(m_time * 5f) * 0.05f;
        boneTransforms[head.Index] = Matrix.CreateTranslation(0, bob, 0);
    }
}
```

### 注册驱动器

```csharp
// 注册驱动器类型
AnimationDriverManager.Register<CustomHeadBobDriver>("CustomHeadBob");

// 在代码中使用
var driver = new CustomHeadBobDriver();
controller.SetDriver("Base", driver);
```

### 在配置中引用

注册后可在 JSON 配置中通过名称引用：

```json
{
  "source": "driver:CustomHeadBob"
}
```
