# 生物模型系统技术文档

本文档详细讲解 Survivalcraft 生物 3D 模型系统的数据结构、组件架构、骨骼动画和渲染管线。

## 目录

1. [整体架构概览](#1-整体架构概览)
2. [核心数据结构](#2-核心数据结构)
3. [模型组件体系](#3-模型组件体系)
4. [骨骼动画机制](#4-骨骼动画机制)
5. [渲染管线](#5-渲染管线)
6. [蒙皮渲染](#6-蒙皮渲染)
7. [实例化渲染优化](#7-实例化渲染优化)
8. [各类生物模型实现](#8-各类生物模型实现)
9. [UI模型显示](#9-ui模型显示)

---

## 1. 整体架构概览

### 1.1 系统架构图

```mermaid
graph TB
    subgraph "引擎层 Engine"
        MD[ModelData<br/>模型数据]
        M[Model<br/>运行时模型]
        MB[ModelBone<br/>骨骼]
        MM[ModelMesh<br/>网格]
        MMP[ModelMeshPart<br/>网格部分]
        MSkin[ModelSkin<br/>蒙皮数据]
        AC[AnimationController<br/>动画控制器]
    end

    subgraph "动画层 Animation"
        AL[AnimationLayer<br/>动画层]
        AP[AnimationPlayer<br/>动画播放器]
        AT[AnimationTransition<br/>过渡混合]
        IKS[IKSolver<br/>IK求解器]
    end

    subgraph "游戏层 Game"
        subgraph "模型组件"
            CM[ComponentModel<br/>基础模型组件]
            CCM[ComponentCreatureModel<br/>生物模型基类]
            HM[ComponentHumanModel]
            FM[ComponentFourLeggedModel]
            BM[ComponentBirdModel]
            FIM[ComponentFishModel]
            SM[ComponentSimpleModel]
            FPM[ComponentFirstPersonModel]
        end

        subgraph "渲染系统"
            SMR[SubsystemModelsRenderer<br/>渲染子系统]
            MSh[ModelShader<br/>着色器]
            IMM[InstancedModelsManager<br/>实例化管理]
        end

        subgraph "UI系统"
            MW[ModelWidget<br/>模型控件]
        end
    end

    MD --> M
    M --> MB
    M --> MM
    M --> MSkin
    MM --> MMP
    AC --> AL
    AL --> AP
    AL --> AT
    AC --> IKS

    CM --> M
    CM --> AC
    CCM --> CM
    HM --> CCM
    FM --> CCM
    BM --> CCM
    FIM --> CCM
    SM --> CM
    FPM --> M

    SMR --> CM
    SMR --> MSh
    SMR --> IMM
    IMM --> M

    MW --> M
```

### 1.2 核心模块职责

| 模块 | 层级 | 职责 |
|------|------|------|
| **Model** | 引擎层 | 运行时模型容器，管理骨骼层级、网格集合和蒙皮数据 |
| **ModelBone** | 引擎层 | 骨骼节点，树形结构存储变换矩阵 |
| **ModelSkin** | 引擎层 | 蒙皮数据：关节索引、逆绑定矩阵、骨骼根节点 |
| **AnimationController** | 动画层 | 动画控制器：状态规则、层级混合、IK、Root Motion |
| **ComponentModel** | 游戏层 | Entity组件，管理模型渲染状态和动画调度 |
| **ComponentCreatureModel** | 游戏层 | 生物模型抽象基类，定义动画参数同步接口 |
| **SubsystemModelsRenderer** | 游戏层 | 模型渲染子系统，管理所有模型的绘制（含蒙皮渲染） |
| **ModelShader** | 游戏层 | 着色器参数封装，支持实例化和蒙皮渲染 |

### 1.3 数据流向

```mermaid
flowchart LR
    A[".dae / .gltf / .glb"] --> B["DaeModelReader / GltfLoader"]
    B --> C[ModelData]
    C --> D["Model.Load"]
    D --> E["Model 运行时对象"]
    E --> F[ComponentModel]
    F --> G{动画类型}
    G -->|glTF + 配置| H["AnimationController"]
    G -->|简单播放| I["AnimationPlayer"]
    G -->|旧过程式| J["AnimateCreature()"]
    H --> K[ComputeBoneTransforms]
    I --> K
    J --> L[SetBoneTransform]
    K --> M[ProcessBoneHierarchy]
    L --> M
    M --> N[SubsystemModelsRenderer]
    N --> O{蒙皮?}
    O -->|是| P["DrawSkinnedModel"]
    O -->|否| Q["DrawInstancedModels"]
    P --> R[GPU 渲染]
    Q --> R
```

---

## 2. 核心数据结构

### 2.1 ModelData - 模型数据容器

`ModelData` 是模型文件的内存表示，用于序列化和反序列化：

```cs
public class ModelData {
    public List<ModelBoneData> Bones = [];
    public List<ModelMeshData> Meshes = [];
    public List<ModelBuffersData> Buffers = [];
}
```

**数据结构关系**：

```mermaid
erDiagram
    ModelData ||--o{ ModelBoneData : contains
    ModelData ||--o{ ModelMeshData : contains
    ModelData ||--o{ ModelBuffersData : contains
    ModelMeshData ||--o{ ModelMeshPartData : contains
    ModelMeshPartData }o--|| ModelBuffersData : references
```

### 2.2 ModelBoneData - 骨骼数据

```cs
public class ModelBoneData {
    public string Name;
    public int ParentBoneIndex;    // -1 表示根骨骼
    public Matrix Transform;
}
```

### 2.3 ModelMeshData - 网格数据

```cs
public class ModelMeshData {
    public string Name;
    public int ParentBoneIndex;
    public List<ModelMeshPartData> MeshParts;
    public BoundingBox BoundingBox;
}
```

### 2.4 ModelMeshPartData - 网格部分数据

```cs
public class ModelMeshPartData {
    public int BuffersDataIndex;
    public int StartIndex;
    public int IndicesCount;
    public BoundingBox BoundingBox;
}
```

### 2.5 ModelBuffersData - 缓冲数据

```cs
public class ModelBuffersData {
    public VertexDeclaration VertexDeclaration;
    public byte[] Vertices = [];
    public byte[] Indices = [];
}
```

### 2.6 Model - 运行时模型

```cs
public class Model : IDisposable {
    public ModelBone m_rootBone;
    public List<ModelBone> m_bones = [];
    public List<ModelMesh> m_meshes = [];
    public ModelData ModelData { get; set; }

    // 蒙皮数据（glTF 模型）
    public ModelSkin Skin { get; set; }
    public bool HasSkin => Skin != null;

    // 动画数据（glTF 模型）
    public List<ModelAnimation> Animations { get; set; }
    public bool HasAnimations => Animations.Count > 0;

    public ModelBone FindBone(string name, bool throwIfNotFound = true);
    public ModelMesh FindMesh(string name, bool throwIfNotFound = true);
    public ModelBone NewBone(string name, Matrix transform, ModelBone parentBone);
    public void CopyAbsoluteBoneTransformsTo(Matrix[] absoluteTransforms);
}
```

### 2.7 ModelSkin - 蒙皮数据

glTF 模型中的蒙皮信息，包含 GPU 蒙皮渲染所需的骨骼绑定数据：

```cs
public class ModelSkin {
    public int[] JointIndices;              // 关节骨骼索引
    public List<ModelBone> Joints;          // 运行时骨骼引用
    public Matrix[] InverseBindMatrices;    // 逆绑定矩阵
    public int SkeletonRootIndex;           // 骨架根骨骼索引
    public ModelBone SkeletonRoot;          // 骨架根骨骼引用

    public void ResolveJoints(List<ModelBone> bones); // 将索引解析为骨骼引用
}
```

**数据来源**：`GltfBoneConverter.ConvertSkins()` 从 glTF Skin 对象提取关节索引、逆绑定矩阵和骨架根节点。

### 2.8 ModelBone - 骨骼

```cs
public class ModelBone {
    public Model Model { get; set; }
    public int Index { get; set; }
    public string Name { get; set; }
    public Matrix Transform { get; set; }
    public ModelBone ParentBone { get; set; }
    public ReadOnlyList<ModelBone> ChildBones;
}
```

### 2.9 ModelMesh / ModelMeshPart - 网格

```cs
public class ModelMesh : IDisposable {
    public string Name { get; set; }
    public ModelBone ParentBone { get; set; }
    public BoundingBox BoundingBox;
    public ReadOnlyList<ModelMeshPart> MeshParts;
}

public class ModelMeshPart : IDisposable {
    public VertexBuffer VertexBuffer { get; set; }
    public IndexBuffer IndexBuffer { get; set; }
    public int StartIndex { get; set; }
    public int IndicesCount { get; set; }
    public BoundingBox BoundingBox;
    public string TexturePath;
}
```

---

## 3. 模型组件体系

### 3.1 继承层次

```mermaid
classDiagram
    class Component {
        +Entity Entity
        +Project Project
    }

    class ComponentModel {
        +Model Model
        +AnimationController AnimationController
        +AnimationPlayer m_animationPlayer
        +Matrix?[] m_boneTransforms
        +Matrix[] AbsoluteBoneTransformsForCamera
        +float Transparent
        +float ModelScale
        +Texture2D TextureOverride
        +ModelRenderingMode RenderingMode
        +SetModel(Model)
        +Animate()
        +CalculateAbsoluteBonesTransforms(Camera)
        +DrawExtras(Camera)
    }

    class ComponentCreatureModel {
        +ComponentCreature m_componentCreature
        +float MovementAnimationPhase
        +float DeathPhase
        +Vector3? LookAtOrder
        +bool AttackOrder
        +bool FeedOrder
        +Animate()
        +SyncAnimationParameters()
        +HandleAnimationEvent(AnimationEvent)
        +abstract AnimateCreature()
        +Update(float dt)
    }

    class ComponentHumanModel
    class ComponentFourLeggedModel
    class ComponentBirdModel
    class ComponentFishModel
    class ComponentSimpleModel

    Component <|-- ComponentModel
    ComponentModel <|-- ComponentCreatureModel
    ComponentModel <|-- ComponentSimpleModel
    ComponentCreatureModel <|-- ComponentHumanModel
    ComponentCreatureModel <|-- ComponentFourLeggedModel
    ComponentCreatureModel <|-- ComponentBirdModel
    ComponentCreatureModel <|-- ComponentFishModel
```

### 3.2 ComponentModel - 基础模型组件

`ComponentModel` 是所有模型组件的基类，负责模型加载、动画调度和骨骼变换计算。

```cs
public class ComponentModel : Component {
    public Model m_model;
    public AnimationController AnimationController { get; private set; }
    public AnimationPlayer m_animationPlayer;
    public Matrix?[] m_boneTransforms;
    public Matrix[] AbsoluteBoneTransformsForCamera;
    public float m_boundingSphereRadius;
    public float Transparent { get; set; }
    public float ModelScale { get; set; }
    public Vector3 ModelOffset { get; set; }
    public Texture2D TextureOverride { get; set; }
    public ModelRenderingMode RenderingMode { get; set; }
    public bool CastsShadow { get; set; }
    public bool IsVisibleForCamera { get; set; }
    public bool Animated { get; set; }
}
```

### 3.3 动画调度优先级

#### ComponentCreatureModel.Animate() 重写

`ComponentCreatureModel` 重写了 `Animate()`，在调用 `base.Animate()` 之前同步参数：

```cs
// ComponentCreatureModel.Animate()
public override void Animate() {
    // 0. 同步动画参数到 AnimationController
    SyncAnimationParameters();

    // 1. 调用基类 Animate()（处理 AnimationController / AnimationPlayer / Mod hooks）
    base.Animate();

    // 2. glTF 模型：将实体变换（位置+旋转）应用到根骨骼
    if (Animated && (Model.HasSkin || Model.HasAnimations)) {
        // 计算根骨骼变换 * 实体变换 * 缩放 * 旋转修正
        m_boneTransforms[Model.RootBone.Index] = rootTransform * entityTransform;
    }

    // 3. 若未被上述任何系统处理，回退到旧过程式动画
    if (!Animated) {
        AnimateCreature();  // 子类实现（如 ComponentFourLeggedModel）
    }
}
```

#### ComponentModel.Animate() 基类

```cs
public virtual void Animate() {
    // 1. Mod 钩子（最先执行，可设置 Animated = true 跳过后续处理）
    ModsManager.HookAction("OnAnimateModel", ...);

    // 2. AnimationController（新系统，glTF 配置驱动）
    if (AnimationController != null) {
        AnimationController.Update(Time.FrameDuration);
        AnimationController.ComputeBoneTransforms(m_boneTransforms);
        Animated = true;
        return;
    }

    // 3. AnimationPlayer（简单播放）
    if (m_animationPlayer != null && m_animationPlayer.IsPlaying) {
        m_animationPlayer.Update(Time.FrameDuration);
        m_animationPlayer.SampleBoneTransforms(m_boneTransforms);
        Animated = true;
        return;
    }
}
```
}
```

### 3.4 模型加载与控制器创建

`ComponentModel.SetModel()` 按优先级创建动画控制器：

```cs
public virtual void SetModel(Model model) {
    m_model = model;
    if (m_model != null) {
        // 1. AnimationConfigPath（JSON 配置文件）→ 创建 AnimationController
        if (!string.IsNullOrEmpty(AnimationConfigJson)) {
            var loader = new AnimationConfigLoader();
            AnimationConfig config = loader.LoadFromJsonNode(...);
            AnimationController = loader.CreateController(config, m_model);
        }
        // 2. AnimationTemplateName → 从模板创建
        else if (!string.IsNullOrEmpty(AnimationTemplateName)) {
            AnimationController = new AnimationController(m_model, AnimationTemplateName);
        }
        // 3. 自动播放第一个动画
        else if (m_model.HasAnimations) {
            m_animationPlayer = new AnimationPlayer();
            m_animationPlayer.SetAnimation(m_model, m_model.Animations[0]);
            m_animationPlayer.Play(loop: true);
        }
    }
}
```

### 3.5 骨骼变换处理

`ProcessBoneHierarchy` 根据模型类型采用不同的骨骼覆盖策略：

```cs
public virtual void ProcessBoneHierarchy(ModelBone bone, Matrix currentTransform, Matrix[] transforms) {
    Matrix m = bone.Transform;
    if (m_boneTransforms[bone.Index].HasValue) {
        // glTF 蒙皮模型 / AnimationPlayer：完整变换替换
        if (Model.HasSkin || m_animationPlayer?.IsPlaying == true) {
            m = m_boneTransforms[bone.Index].Value;
        }
        // 旧过程式模型：保留原始平移，仅覆盖旋转
        else {
            Vector3 translation = m.Translation;
            m.Translation = Vector3.Zero;
            m *= m_boneTransforms[bone.Index].Value;
            m.Translation += translation;
        }
    }
    Matrix.MultiplyRestricted(ref m, ref currentTransform, out transforms[bone.Index]);

    foreach (ModelBone child in bone.ChildBones) {
        ProcessBoneHierarchy(child, transforms[bone.Index], transforms);
    }
}
```

### 3.6 ComponentCreatureModel - 生物模型基类

```cs
public abstract class ComponentCreatureModel : ComponentModel, IUpdateable {
    public ComponentCreature m_componentCreature;
    public float MovementAnimationPhase { get; set; }
    public float DeathPhase { get; set; }
    public float Bob { get; set; }

    // 行为指令
    public Vector3? LookAtOrder { get; set; }
    public bool LookRandomOrder { get; set; }
    public float HeadShakeOrder { get; set; }
    public bool AttackOrder { get; set; }
    public bool FeedOrder { get; set; }

    // 动画事件
    public bool IsAttackHitMoment { get; set; }

    // 重写 Animate：同步参数 → 基类动画 → glTF 根骨骼 → 旧过程式
    public override void Animate();

    // 设置模型时自动订阅/取消订阅事件
    public override void SetModel(Model model);

    // 新系统：同步动画参数到 AnimationController
    public virtual void SyncAnimationParameters();

    // 动画事件处理（基类已处理 AttackHit/Footstep/AttackStart/AttackEnd）
    public virtual void HandleAnimationEvent(AnimationEvent animationEvent);

    // 旧系统：过程式骨骼动画（子类实现）
    public abstract void AnimateCreature();
}
```

#### SyncAnimationParameters()

每帧在动画更新前调用。**基类实现已同步大量内置参数**：

| 参数 | 类型 | 说明 |
|------|------|------|
| `Speed` | float | 前向速度（优先使用 SlipSpeed） |
| `SpeedAbs` | float | 速度绝对值 |
| `MovementPhase` | float | 移动动画相位 |
| `DeathPhase` | float | 死亡阶段 0-1 |
| `DeathCauseOffset` | Vector3 | 死因偏移方向 |
| `IsDead` | bool | 是否死亡 |
| `Health` | float | 当前生命值 |
| `WalkSpeed` | float | 行走速度 |
| `IsFlying` | bool | 是否飞行 |
| `IsCreativeFly` | bool | 是否创造模式飞行 |
| `IsInWater` | bool | 是否在水中 |
| `IsOnGround` | bool | 是否在地面 |
| `ImmersionFactor` | float | 浸水程度 |
| `LookAngleX/Y` | float | 注视角度（弧度） |
| `BodyHeight` | float | 身体高度 |
| `Position` | Vector3 | 世界位置 |
| `Rotation` | Vector3 | 完整旋转（YawPitchRoll） |
| `RotationY` | float | Y 轴旋转 |
| `BodyForward` | Vector3 | 身体前方方向 |
| `BodyRight` | Vector3 | 身体右侧方向 |
| `IsAttacking` | bool | 是否攻击中 |
| `IsFeeding` | bool | 是否进食中 |
| `GameTime` | float | 游戏时间 |

模组开发者的自定义组件应调用 `base.SyncAnimationParameters()` 后添加自定义参数。

#### HandleAnimationEvent()

基类 `SetModel()` 自动将 `AnimationController.OnAnimationEvent` 订阅到 `HandleAnimationEvent`。子类 override 此方法处理自定义事件，调用 `base.HandleAnimationEvent()` 保留内置事件处理。

---

## 4. 骨骼动画机制

系统支持三种动画方式，按优先级使用：

### 4.1 AnimationController（新系统）

glTF 模型 + JSON 动画配置的核心动画系统。基于层级混合、状态规则和参数驱动。

**架构**：

```mermaid
flowchart TD
    A["SyncAnimationParameters()<br/>(C# 代码设置参数)"] --> B["AnimationParameters"]
    B --> C["StateRuleEvaluator<br/>(评估状态规则)"]
    C --> D["AnimationLayer[]<br/>(各层独立更新)"]
    D --> E["AnimationBlender<br/>(层混合)"]
    E --> F["IKSolver<br/>(IK 后处理)"]
    F --> G["ComputeBoneTransforms<br/>(输出骨骼变换)"]
    G --> H["m_boneTransforms"]
```

**关键概念**：
- **模板（Template）**：预定义层级和状态轨道（Simple/FourLegged/Human/Bird/Fish/FlightlessBird）
- **层级（Layer）**：独立的动画播放上下文，支持 Override/Additive 混合
- **状态规则（State Rules）**：条件表达式 → 动画映射的有序规则列表
- **参数（Parameters）**：类型化的运行时值，驱动表达式和状态规则

详细用法参见：
- [GltfCreatureModTutorial.md](GltfCreatureModTutorial.md) — 创建 glTF 生物模组的教程
- [AnimationConfigReference.md](AnimationConfigReference.md) — 动画配置 JSON 格式参考
- [AnimationAdvancedTopics.md](AnimationAdvancedTopics.md) — IK、Root Motion 等高级主题

### 4.2 AnimationPlayer（简单播放）

自动播放模型文件中的第一个动画，支持循环播放和骨骼采样。用于没有动画配置的简单场景。

### 4.3 旧过程式动画

`ComponentCreatureModel.AnimateCreature()` 的子类实现通过 `SetBoneTransform()` 手动设置骨骼旋转。这是游戏原有 `.dae` 模型使用的动画方式。详见[第 8 节](#8-各类生物模型实现)。

---

## 5. 渲染管线

### 5.1 SubsystemModelsRenderer 架构

```cs
public class SubsystemModelsRenderer : Subsystem, IDrawable {
    // 模型数据缓存
    public Dictionary<ComponentModel, ModelData> m_componentModels = [];

    // 渲染队列（按渲染模式分组）
    public List<ModelData>[] m_modelsToDraw = [[], [], [], []];

    // 着色器（非蒙皮）
    public static ModelShader ShaderOpaque;
    public static ModelShader ShaderAlphaTested;

    // 着色器（蒙皮）
    public static ModelShader ShaderSkinnedOpaque;
    public static ModelShader ShaderSkinnedAlphaTested;

    // 蒙皮渲染缓冲
    public Matrix[] m_jointMatricesBuffer;
    public readonly List<ModelData> m_nonSkinnedModelsBuffer = [];
    public readonly List<ModelData> m_skinnedModelsBuffer = [];

    // 绘制顺序
    public int[] m_drawOrders = [-10000, 1, 99, 201];
}
```

### 5.2 渲染流程

```mermaid
sequenceDiagram
    participant Camera
    participant SMR as SubsystemModelsRenderer
    participant CM as ComponentModel
    participant GPU

    Note over Camera: 帧开始

    Camera->>SMR: Draw(drawOrder = -10000)
    Note over SMR: 准备阶段
    loop 每个模型
        SMR->>CM: CalculateIsVisible(camera)
        alt 可见
            SMR->>CM: Animate()
            SMR->>CM: CalculateAbsoluteBonesTransforms(camera)
            SMR->>SMR: 按 RenderingMode 分入队列
        end
    end

    Camera->>SMR: Draw(drawOrder = 1)
    Note over SMR: AlphaThreshold 模式
    SMR->>SMR: 分离蒙皮/非蒙皮模型
    SMR->>GPU: DrawInstancedModels (非蒙皮)
    SMR->>GPU: DrawSkinnedModel (蒙皮)

    Camera->>SMR: Draw(drawOrder = 99)
    Note over SMR: TransparentBeforeWater 模式
    SMR->>GPU: DrawInstancedModels / DrawSkinnedModel

    Camera->>SMR: Draw(drawOrder = 201)
    Note over SMR: TransparentAfterWater 模式
    SMR->>GPU: DrawInstancedModels / DrawSkinnedModel
```

### 5.3 渲染模式

```cs
public enum ModelRenderingMode {
    Solid,                    // 不透明
    AlphaThreshold,           // Alpha测试
    TransparentBeforeWater,   // 水前透明
    TransparentAfterWater     // 水后透明
}
```

### 5.4 蒙皮/非蒙皮分流

在每个绘制 Pass 中，`DrawModels()` 将模型分为两组：

```cs
void DrawModels(List<ModelData> models, ...) {
    m_nonSkinnedModelsBuffer.Clear();
    m_skinnedModelsBuffer.Clear();

    foreach (var modelData in models) {
        if (modelData.ComponentModel.Model?.HasSkin == true)
            m_skinnedModelsBuffer.Add(modelData);
        else
            m_nonSkinnedModelsBuffer.Add(modelData);
    }

    DrawInstancedModels(m_nonSkinnedModelsBuffer, ...);
    foreach (var skinned in m_skinnedModelsBuffer)
        DrawSkinnedModel(skinned, ...);
}
```

---

## 6. 蒙皮渲染

glTF 模型使用 GPU 蒙皮（GPU Skinning）在顶点着色器中完成骨骼变形。

### 6.1 着色器变体

系统创建 4 个着色器变体：

| 变体 | 用途 | 实例化 | 关节数 |
|------|------|--------|--------|
| `ShaderOpaque` | 非蒙皮不透明 | 支持（最多 64 实例） | — |
| `ShaderAlphaTested` | 非蒙皮 Alpha 测试 | 支持 | — |
| `ShaderSkinnedOpaque` | 蒙皮不透明 | 不支持（1 实例） | MaxJointsCount |
| `ShaderSkinnedAlphaTested` | 蒙皮 Alpha 测试 | 不支持（1 实例） | MaxJointsCount |

`MaxJointsCount` 在运行时根据 `GL_MAX_VERTEX_UNIFORM_VECTORS` 计算，上限 128。

### 6.2 顶点数据

glTF 模型的顶点声明包含蒙皮权重属性：

```
Position (Vector3) | Normal (Vector3) | TexCoord (Vector2)
| BlendIndices (Vector4) | BlendWeights (Vector4)
```

- **BlendIndices**（`a_joints`）：影响该顶点的 4 个关节索引
- **BlendWeights**（`a_weights`）：对应的 4 个权重值（归一化后总和为 1）

### 6.3 关节矩阵计算

`SubsystemModelsRenderer.CalculateJointMatrices()` 为每个关节计算最终的蒙皮矩阵：

```cs
for (int i = 0; i < skin.Joints.Count; i++) {
    ModelBone joint = skin.Joints[i];

    // 1. 获取关节在视图空间中的世界变换
    Matrix jointWorld = AbsoluteBoneTransformsForCamera[joint.Index];

    // 2. 转回世界空间
    Matrix jointWorldSpace = jointWorld * invertedView;

    // 3. 转换到 glTF 坐标空间（去除根骨骼坐标修正）
    Matrix jointWorldGlTF = jointWorldSpace * invRootBoneTransform;

    // 4. 应用逆绑定矩阵
    // 最终矩阵 = InverseBind * JointWorld
    output[i] = skin.InverseBindMatrices[i] * jointWorldGlTF * rootBoneTransform;
}
```

### 6.4 顶点着色器蒙皮

```glsl
#ifdef USE_SKINNING
uniform mat4 u_jointMatrices[MAX_JOINTS_COUNT];
attribute vec4 a_joints;
attribute vec4 a_weights;

mat4 getSkinningMatrix() {
    vec4 joints = a_joints;
    vec4 weights = a_weights;
    mat4 skin = mat4(0.0);
    skin += weights.x * u_jointMatrices[int(joints.x)];
    skin += weights.y * u_jointMatrices[int(joints.y)];
    skin += weights.z * u_jointMatrices[int(joints.z)];
    skin += weights.w * u_jointMatrices[int(joints.w)];
    return skin;
}
#endif

void main() {
    vec4 pos = vec4(a_position, 1.0);
    vec3 norm = a_normal;

    #ifdef USE_SKINNING
    mat4 skinMatrix = getSkinningMatrix();
    pos = skinMatrix * pos;
    norm = mat3(skinMatrix) * norm;
    #endif

    // 光照、雾效、变换...
}
```

4 权重线性混合蒙皮（Linear Blend Skinning），每个顶点最多受 4 个关节影响。

### 6.5 蒙皮模型渲染流程

```mermaid
flowchart TD
    A[DrawSkinnedModel] --> B[CalculateJointMatrices]
    B --> C[上传关节矩阵到 Uniform]
    C --> D["设置 World = ViewMatrix<br/>View = Identity"]
    D --> E[遍历 Meshes/MeshParts]
    E --> F["Display.DrawIndexed<br/>(单实例)"]
```

蒙皮模型不使用实例化渲染——每个模型单独绘制，因为每个模型的关节矩阵不同。

---

## 7. 实例化渲染优化

### 7.1 适用范围

仅**非蒙皮模型**（`.dae` 模型或无蒙皮的 glTF 模型）支持实例化渲染。蒙皮模型逐个绘制。

### 7.2 InstancedModelData

```cs
public class InstancedModelData {
    // 实例化顶点声明
    public static readonly VertexDeclaration VertexDeclaration = new(
        VertexElement(0,  Vector3, Position),
        VertexElement(12, Vector3, Normal),
        VertexElement(24, Vector2, TextureCoordinate),
        VertexElement(32, Single,  Instance)  // 骨骼索引作为实例 ID
    );
}
```

`InstancedModelsManager` 将普通模型转换为扁平的实例化顶点缓冲，骨骼索引作为实例 ID。着色器通过 `u_worldMatrix[instance]` 查找对应骨骼的世界变换矩阵。

### 7.3 实例化渲染流程

```mermaid
flowchart TD
    A[获取 InstancedModelData] --> B[设置骨骼世界变换数组]
    B --> C[设置材质/光照参数]
    C --> D["单次 DrawIndexed 调用<br/>(最多 64 实例)"]
    D --> E[着色器根据 Instance ID 选择变换矩阵]
```

---

## 8. 各类生物模型实现

### 8.1 旧过程式模型（.dae 模型）

以下模型组件使用旧的骨骼变换覆盖机制（`SetBoneTransform()`），适用于游戏内置的 `.dae` 模型。**新 glTF 模型不需要这些实现**——它们通过 AnimationController 和 JSON 配置驱动动画。

#### ComponentFourLeggedModel - 四足动物

```cs
public class ComponentFourLeggedModel : ComponentCreatureModel {
    public ModelBone m_bodyBone, m_neckBone, m_headBone;
    public ModelBone m_leg1Bone, m_leg2Bone, m_leg3Bone, m_leg4Bone;
    public Gait m_gait;  // Walk, Trot, Canter
}
```

步态判断逻辑：根据速度选择 Walk/Trot/Canter，通过正弦函数驱动腿部相位。

| 步态 | 腿1 | 腿2 | 腿3 | 腿4 | 说明 |
|------|-----|-----|-----|-----|------|
| Walk | 0° | 180° | 90° | 270° | 对角交替 |
| Trot | 0° | 180° | 180° | 0° | 同侧同步 |
| Canter | 0° | 90° | 54° | 144° | 跑步节奏 |

#### ComponentHumanModel - 人形模型

```cs
public class ComponentHumanModel : ComponentCreatureModel {
    public ModelBone m_bodyBone, m_headBone;
    public ModelBone m_leg1Bone, m_leg2Bone;
    public ModelBone m_hand1Bone, m_hand2Bone;
}
```

支持行走、出拳、潜行、划船等动画，通过正弦函数和角度插值驱动。

#### ComponentBirdModel - 鸟类模型

```cs
public class ComponentBirdModel : ComponentCreatureModel {
    public ModelBone m_bodyBone, m_neckBone, m_headBone;
    public ModelBone m_leg1Bone, m_leg2Bone;
    public ModelBone m_wing1Bone, m_wing2Bone;
}
```

支持飞行和地面行走，飞行时翅膀扇动、腿部收起。

#### ComponentFishModel - 鱼类模型

```cs
public class ComponentFishModel : ComponentCreatureModel {
    public ModelBone m_bodyBone;
    public ModelBone m_tail1Bone, m_tail2Bone;
    public ModelBone m_jawBone;
}
```

支持垂直尾（鲨鱼式左右摆动）和水平尾（上下摆动）。

### 8.2 glTF 模型

glTF 模型不使用上述过程式动画类。模组开发者可以：

1. **纯数据驱动**：在数据库模板中使用 `FourLeggedModel` 等内置组件，配合 `AnimationConfigPath` 参数指向 JSON 配置文件。无需 C# 代码。

2. **自定义组件**：继承 `ComponentCreatureModel`，重写 `SyncAnimationParameters()` 将游戏状态同步到 AnimationController 参数。在数据库模板中通过 `Class` 参数指定自定义类名。

详细指南参见 [GltfCreatureModTutorial.md](GltfCreatureModTutorial.md)。

---

## 9. UI模型显示

### 9.1 ModelWidget

`ModelWidget` 用于在 UI 界面中显示 3D 模型，支持蒙皮和非蒙皮模型。

```cs
public class ModelWidget : Widget {
    public List<Model> Models = new();
    public Dictionary<Model, Matrix?[]> m_boneTransforms;
    public Dictionary<Model, Matrix[]> m_absoluteBoneTransforms;
    public Dictionary<Model, Texture2D> Textures;

    public bool IsPerspective { get; set; }
    public Vector3 ViewPosition { get; set; }
    public Vector3 ViewTarget { get; set; }
    public float ViewFov { get; set; }
    public Vector3 OrthographicFrustumSize;
    public Vector3 AutoRotationVector { get; set; }
    public TransformedShader CustomShader { get; set; }
}
```

### 9.2 蒙皮模型 UI 渲染

ModelWidget 内部也区分蒙皮和非蒙皮模型（`Model.HasSkin`），分别使用对应的着色器绘制。

---

## 附录：模型骨骼命名约定

### 旧 .dae 模型骨骼命名

| 模型类型 | 骨骼名称 | 说明 |
|----------|----------|------|
| Human | Body, Head, Hand1, Hand2, Leg1, Leg2 | 人形六骨骼 |
| FourLegged | Body, Neck(可选), Head, Leg1-4 | 四足五/六骨骼 |
| Bird | Body, Neck, Head, Leg1, Leg2, Wing1(可选), Wing2(可选) | 鸟类五/七骨骼 |
| Fish | Body, Tail1, Tail2, Jaw(可选) | 鱼类三/四骨骼 |

### glTF 模型骨骼

glTF 模型的骨骼名称由建模软件决定，没有固定命名约定。在动画配置中通过骨骼名称引用（如 LookAt 驱动器的 `TargetBoneName`、IK 链的 `endBoneName`、层级 `bones` 过滤列表）。

---

## 相关文档

- [GltfCreatureModTutorial.md](GltfCreatureModTutorial.md) — glTF 生物模组开发教程
- [AnimationConfigReference.md](AnimationConfigReference.md) — 动画配置 JSON 参考
- [AnimationAdvancedTopics.md](AnimationAdvancedTopics.md) — 动画系统高级主题（IK、Root Motion、表达式）
