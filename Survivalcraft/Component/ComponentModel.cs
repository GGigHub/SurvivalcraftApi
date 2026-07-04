using System.Text.Json.Nodes;
using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Serialization;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class ComponentModel : Component {
        public SubsystemSky m_subsystemSky;

        public bool IsSet;
        public bool Animated;
        public bool IsExtrasDrawn;

        public ComponentFrame m_componentFrame;

        public Model m_model;

        public Matrix?[] m_boneTransforms;

        public float m_boundingSphereRadius;

        public AnimationPlayer m_animationPlayer;

        /// <summary>
        /// 动画控制器
        /// </summary>
        public AnimationController AnimationController { get; private set; }

        /// <summary>
        /// 动画模板名称
        /// </summary>
        public string AnimationTemplateName { get; private set; }

        /// <summary>
        /// 动画配置文件路径（可选）
        /// 如果指定，将使用 AnimationConfigLoader 加载配置并创建控制器
        /// </summary>
        public string AnimationConfigJson { get; private set; }

        /// <summary>
        ///     模型偏移
        /// </summary>
        public Vector3 ModelOffset { get; set; }

        /// <summary>
        ///     模型透明度
        /// </summary>
        public float Transparent { get; set; }

        /// <summary>
        ///     模型大小缩放
        /// </summary>
        public float ModelScale { get; set; }

        /// <summary>
        ///     纹理路径
        /// </summary>
        public string TextureRoute { get; set; }

        /// <summary>
        ///     模型路径
        /// </summary>
        public string ModelRoute { get; set; }

        public float? Opacity { get; set; }

        public Vector3? DiffuseColor { get; set; }

        public Vector4? EmissionColor { get; set; }

        public Model Model {
            get => m_model;
            set => SetModel(value);
        }

        public Texture2D TextureOverride { get; set; }

        public virtual Func<bool> OnAnimate { get; set; }

        public bool CastsShadow { get; set; }

        public int PrepareOrder { get; set; }

        public virtual ModelRenderingMode RenderingMode { get; set; }

        public int[] MeshDrawOrders { get; set; }

        public bool IsVisibleForCamera { get; set; }

        public Matrix[] AbsoluteBoneTransformsForCamera { get; set; }

        public virtual Matrix? GetBoneTransform(int boneIndex) => m_boneTransforms[boneIndex];

        public virtual void SetBoneTransform(int boneIndex, Matrix? transformation) {
            m_boneTransforms[boneIndex] = transformation * Matrix.CreateTranslation(ModelOffset);
        }

        public virtual void CalculateAbsoluteBonesTransforms(Camera camera) {
            bool flag = false;
            ModsManager.HookAction(
                "OnModelCalculateBones",
                loader => {
                    loader.OnModelCalculateBones(this, camera, out bool skip);
                    flag |= skip;
                    return false;
                }
            );
            if (flag) {
                return;
            }
            // 先计算骨骼的世界变换（不包含视图矩阵）
            ProcessBoneHierarchy(Model.RootBone, Matrix.Identity, AbsoluteBoneTransformsForCamera);

            // 然后应用视图矩阵
            for (int i = 0; i < AbsoluteBoneTransformsForCamera.Length; i++) {
                AbsoluteBoneTransformsForCamera[i] = AbsoluteBoneTransformsForCamera[i] * camera.ViewMatrix;
            }
        }

        public virtual void CalculateIsVisible(Camera camera) {
            bool flag = false;
            ModsManager.HookAction(
                "OnModelCalculateIsVisible",
                loader => {
                    loader.OnModelCalculateIsVisible(this, camera, out bool skip);
                    flag |= skip;
                    return false;
                }
            );
            if (flag) {
                return;
            }
            if (camera.GameWidget.IsEntityFirstPersonTarget(Entity)) {
                IsVisibleForCamera = false;
                return;
            }
            float num = MathUtils.Sqr(m_subsystemSky.VisibilityRange);
            Vector3 vector = m_componentFrame.Position - camera.ViewPosition;
            vector.Y *= m_subsystemSky.VisibilityRangeYMultiplier;
            if (vector.LengthSquared() < num) {
                BoundingSphere sphere = new(m_componentFrame.Position, m_boundingSphereRadius);
                IsVisibleForCamera = camera.ViewFrustum.Intersection(sphere);
            }
            else {
                IsVisibleForCamera = false;
            }
        }

        public virtual void Animate() {
            Animated = false;

            ModsManager.HookAction(
                "OnAnimateModel",
                loader => {
                    loader.OnAnimateModel(this, out bool skip);
                    Animated |= skip;
                    return false;
                }
            );

            // 优先使用动画控制器
            if (AnimationController != null) {
                // 清除上一帧的骨骼变换
                for (int i = 0; i < m_boneTransforms.Length; i++) {
                    m_boneTransforms[i] = null;
                }

                // RootMotion: 同步物理体速度和旋转
                ComponentBody body = AnimationController.HasRootMotion
                    ? Entity.FindComponent<ComponentBody>() : null;
                if (body != null) {
                    AnimationController.Velocity = body.Velocity;
                    AnimationController.EntityRotation = body.Rotation;
                }

                AnimationController.Update(Time.FrameDuration);
                AnimationController.ComputeBoneTransforms(m_boneTransforms);

                // RootMotion: 将冲量/速度写回物理体
                if (body != null && AnimationController.Velocity.HasValue) {
                    body.Velocity = AnimationController.Velocity.Value;
                }

                Animated = true;
            }
            // 后备：简单动画播放
            else if (m_animationPlayer != null && m_animationPlayer.IsPlaying) {
                // 清除上一帧的骨骼变换
                for (int i = 0; i < m_boneTransforms.Length; i++) {
                    m_boneTransforms[i] = null;
                }

                m_animationPlayer.Update(Time.FrameDuration);
                m_animationPlayer.SampleBoneTransforms(m_boneTransforms);
                m_animationPlayer.SamplePointerTargets(Model);
                m_animationPlayer.SampleMorphWeights(Model);

                // 标记动画已处理
                Animated = true;
            }
        }

        public virtual void DrawExtras(Camera camera) {
            IsExtrasDrawn = false;
            ModsManager.HookAction(
                "OnModelDrawExtra",
                loader => {
                    loader.OnModelDrawExtra(this, camera, out bool skip);
                    IsExtrasDrawn |= skip;
                    return false;
                }
            );
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_subsystemSky = Project.FindSubsystem<SubsystemSky>(true);
            m_componentFrame = Entity.FindComponent<ComponentFrame>(true);
            ModelRoute = valuesDictionary.GetValue("ModelName", "");
            string modeltype = valuesDictionary.GetValue("ModelType", "Engine.Graphics.Model");
            CastsShadow = valuesDictionary.GetValue<bool>("CastsShadow");
            TextureRoute = valuesDictionary.GetValue("TextureOverride", "");
            TextureOverride = string.IsNullOrEmpty(TextureRoute) ? null : ContentManager.Get<Texture2D>(TextureRoute);
            PrepareOrder = valuesDictionary.GetValue<int>("PrepareOrder");
            Transparent = valuesDictionary.GetValue("Transparent", 1f);
            ModelScale = valuesDictionary.GetValue("ModelScale", 1f);
            m_boundingSphereRadius = valuesDictionary.GetValue<float>("BoundingSphereRadius");
            // 读取动画配置路径（可选）
            string animationConfigPath = valuesDictionary.GetValue("AnimationConfigPath", "");
            if (!string.IsNullOrEmpty(animationConfigPath)) {
                AnimationConfigJson = ContentManager.Get<string>(animationConfigPath, ".json");
            }
            Type type = TypeCache.FindType(modeltype, true, true);
            Model = (Model)ContentManager.Get(type, ModelRoute);
        }

        public virtual void SetModel(Model model) {
            IsSet = false;
            ModsManager.HookAction(
                "OnSetModel",
                modLoader => {
                    modLoader.OnSetModel(this, model, out IsSet);
                    return false;
                }
            );
            if (IsSet) {
                return;
            }
            m_model = model;
            if (m_model != null) {
                m_boneTransforms = new Matrix?[m_model.Bones.Count];
                AbsoluteBoneTransformsForCamera = new Matrix[m_model.Bones.Count];
                MeshDrawOrders = Enumerable.Range(0, m_model.Meshes.Count).ToArray();

                // 初始化动画控制器
                // 优先级：AnimationConfigPath > AnimationTemplateName > 自动播放
                if (!string.IsNullOrEmpty(AnimationConfigJson)) {
                    // 使用配置文件创建控制器
                    var loader = new AnimationConfigLoader();
                    AnimationConfig config = loader.LoadFromJsonNode(JsonNode.Parse(AnimationConfigJson));
                    AnimationController = loader.CreateController(config, m_model);

                    // 应用动画配置中的模型缩放（覆盖 ValuesDictionary 中的值）
                    if (AnimationController.ModelScale != 1f) {
                        ModelScale *= AnimationController.ModelScale;
                    }
                }
                else if (!string.IsNullOrEmpty(AnimationTemplateName)) {
                    // 使用模板名称创建控制器
                    AnimationController = new AnimationController(m_model, AnimationTemplateName);
                }
                // 不要提交这部分临时测试代码
                else if (m_model.HasAnimations) {
                    m_animationPlayer = new AnimationPlayer();
                    m_animationPlayer.SetAnimation(m_model, m_model.Animations[0]);
                    m_animationPlayer.Play(loop: true);
                }
            }
            else {
                m_boneTransforms = null;
                AbsoluteBoneTransformsForCamera = null;
                MeshDrawOrders = null;
                m_animationPlayer = null;
                AnimationController = null;
            }
        }

        public virtual void ProcessBoneHierarchy(ModelBone modelBone, Matrix currentTransform, Matrix[] transforms) {
            Matrix m = modelBone.Transform;
            if (m_boneTransforms[modelBone.Index].HasValue) {
                // AnimationPlayer/AnimationController 输出完整局部变换（含平移），直接替换
                // DAE 模型通过 SetBoneTransform 设旋转，需要保留原始平移
                bool fullTransform = Model.HasSkin
                    || m_animationPlayer?.IsPlaying == true;
                if (fullTransform) {
                    m = m_boneTransforms[modelBone.Index].Value;
                } else {
                    Vector3 translation = m.Translation;
                    m.Translation = Vector3.Zero;
                    m *= m_boneTransforms[modelBone.Index].Value;
                    m.Translation += translation;
                }
            }
            // 根骨骼统一应用 ModelScale
            if (modelBone == Model.RootBone && ModelScale != 1f) {
                m = Matrix.CreateScale(ModelScale) * m;
            }

            // 骨骼世界变换 = 骨骼局部变换 * 父骨骼世界变换
            Matrix.MultiplyRestricted(ref m, ref currentTransform, out transforms[modelBone.Index]);

            foreach (ModelBone childBone in modelBone.ChildBones) {
                ProcessBoneHierarchy(childBone, transforms[modelBone.Index], transforms);
            }
        }
    }
}