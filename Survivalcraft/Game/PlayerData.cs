using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Engine;
using Game.NetWork;
using Game.NetWork.Packages;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class PlayerData : IDisposable {
        public enum SpawnMode {
            InitialIntro,
            InitialNoIntro,
            Respawn
        }

        public static string fName = "PlayerData";

        public Project m_project;

        public SubsystemTerrain m_subsystemTerrain;

        public SubsystemGameInfo m_subsystemGameInfo;

        public SubsystemSky m_subsystemSky;

        public GameWidget m_gameWidget;

        public StateMachine m_stateMachine = new();

        public PlayerClass m_playerClass;

        public string m_name;

        public SpawnMode m_spawnMode;

        public double? m_playerDeathTime;

        public double m_terrainWaitStartTime;
        public double TerrainMaxWaitTime { get; set; } = 15;

        public SpawnDialog m_spawnDialog;

        public float m_progress;
        public int PlayerIndex { get; set; }

        // 客户端获取到其他玩家的PlayerGuid是他们各自的TokenID，被混淆过了
        // 客户端自己的PlayerGuid，以及服务端上所有玩家的PlayerGuid都未经混淆
        // 以上的PlayerGuid指的是ComponentPlayer,PlayerData,Client中的PlayerGuid
        public Guid PlayerGUID { get; set; } = CommonLib.Net.Self.PlayerGuid;

        public Client Client => CommonLib.Net.GetClientByGUID(PlayerGUID);

        public byte? ClientId => Client?.ID;

        public bool ServerManager { get; set; }

        public Vector3? ClientCachePosition;

        public bool isSetStatus = false;

        public bool IsDead;

        public GameMode PlayerGameMode {
            get;
            set;
        }

        public bool IsMainPlayer { get; set; }

        public SubsystemGameWidgets SubsystemGameWidgets { get; set; }

        public SubsystemPlayers SubsystemPlayers { get; set; }

        public ComponentPlayer ComponentPlayer { get; set; }

        public Entity LastDeadPlayer { get; set; }

        public GameWidget GameWidget {
            get; set;
        }

        public Vector3 SpawnPosition { get; set; }

        public double FirstSpawnTime { get; set; }

        public double LastSpawnTime { get; set; }

        public int SpawnsCount { get; set; }

        public string Name {
            get => m_name;
            set {
                if (value != m_name) {
                    m_name = value;
                    IsDefaultName = false;
                }
            }
        }

        public bool IsDefaultName { get; set; }

        public PlayerClass PlayerClass {
            get => m_playerClass;
            set {
                if (SubsystemPlayers.PlayersData.Contains(this)) {
                    throw new InvalidOperationException(LanguageControl.Get(fName, 1));
                }
                m_playerClass = value;
            }
        }

        public float Level { get; set; }
        public bool readyToRestart;
        public bool ReduceLevelUponDeath = true;
        public string CharacterSkinName { get; set; }

        public WidgetInputDevice InputDevice { get; set; }

        public bool IsReadyForPlaying {
            get {
                if (m_stateMachine.CurrentState != "Playing") {
                    return m_stateMachine.CurrentState == "PlayerDead";
                }
                return true;
            }
        }

        public double m_initialSpawnWaitTime = 0;
        public double m_respawnWaitTime = 2.0;

        public bool IsTimeReadyToSpawn {
            get {
                if (m_spawnMode == SpawnMode.Respawn) {
                    return Time.FrameStartTime - m_terrainWaitStartTime > m_respawnWaitTime;
                }
                return Time.FrameStartTime - m_terrainWaitStartTime > m_initialSpawnWaitTime;
            }
        }

        public PlayerData(Project project) {
            m_project = project;
            SubsystemPlayers = project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
            SubsystemGameWidgets = project.FindSubsystem<SubsystemGameWidgets>(throwOnError: true);
            m_subsystemTerrain = project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_subsystemGameInfo = project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
            m_subsystemSky = project.FindSubsystem<SubsystemSky>(throwOnError: true);
            m_playerClass = PlayerClass.Male;
            PlayerGameMode = GameMode.Follow;
            Level = 1f;
            FirstSpawnTime = -1.0;
            LastSpawnTime = -1.0;
            RandomizeCharacterSkin();
            ResetName();
            InputDevice = WidgetInputDevice.None;
            m_stateMachine.AddState("FirstUpdate", null, delegate
            {
                //说明：没有在线的玩家让其离线
                if ((CommonLib.WorkType != WorkType.Local && CommonLib.Net.GetClientByGUID(PlayerGUID) == null) || (CommonLib.WorkType == WorkType.Local && !IsMainPlayer)) {
                    m_stateMachine.TransitionTo("MakePlayerOffline");
                }
                else if (ComponentPlayer != null) {
                    UpdateSpawnDialog(string.Format(LanguageControl.Get(fName, 4), Name, MathUtils.Floor(Level)), null, 0f, resetProgress: true);
                    m_stateMachine.TransitionTo("WaitForTerrain");
                }
                else {
#if DEBUG
                    Log.Debug($"准备生成玩家 ID: {PlayerGUID}");
#endif
                    m_stateMachine.TransitionTo("PrepareSpawn");
                }
            }, null);
            m_stateMachine.AddState("MakePlayerOffline", delegate { AsyncDispatcher.Dispatch(() => SubsystemPlayers.MakePlayerOffline(PlayerGUID, false)); }, null, null);
            m_stateMachine.AddState("PrepareSpawn", delegate
            {
                if (SpawnPosition == Vector3.Zero) {
                    if (CommonLib.WorkType != WorkType.Local && m_subsystemGameInfo.WorldSettings.RandomSpawnPosition) SubsystemPlayers.GlobalSpawnPosition = Vector3.Zero;
                    if (SubsystemPlayers.GlobalSpawnPosition == Vector3.Zero) {
                        PlayerData playerData = SubsystemPlayers.PlayersData.FirstOrDefault((PlayerData pd) => pd.SpawnPosition != Vector3.Zero);
                        if (playerData != null && CommonLib.WorkType == WorkType.Local) {
                            SpawnPosition = playerData.SpawnPosition;
                            m_spawnMode = SpawnMode.InitialNoIntro;
                        }
                        else {
                            SpawnPosition = m_subsystemTerrain.TerrainContentsGenerator.FindCoarseSpawnPosition();
#if DEBUG
                            Log.Information($"生成出生点{SpawnPosition}");
#endif
                            m_spawnMode = CommonLib.WorkType == WorkType.Server ? SpawnMode.InitialNoIntro : SpawnMode.InitialIntro;
                        }
                        SubsystemPlayers.GlobalSpawnPosition = SpawnPosition;
                    }
                    else {
                        SpawnPosition = SubsystemPlayers.GlobalSpawnPosition;
                        m_spawnMode = SpawnMode.InitialNoIntro;
                    }
                }
                else {
                    m_spawnMode = SpawnMode.Respawn;
                }
                if (m_spawnMode == SpawnMode.Respawn) {
                    if (CommonLib.WorkType != WorkType.Client) UpdateSpawnDialog(string.Format(LanguageControl.Get(fName, 2), Name, MathUtils.Floor(Level)), LanguageControl.Get(fName, 3), 0f, resetProgress: true);
                    else UpdateSpawnDialog(string.Format("连接{0}的世界中 (等级 {1})", Name, MathUtils.Floor(Level)), LanguageControl.Get(fName, 3), 0f, resetProgress: true);
                }
                else {
                    UpdateSpawnDialog(string.Format(LanguageControl.Get(fName, 4), Name, MathUtils.Floor(Level)), null, 0f, resetProgress: true);
                }
                //说明，客户端非主玩家不执行updateLocation
                if (!(CommonLib.WorkType == WorkType.Client && !IsMainPlayer)) {
                    m_subsystemTerrain.TerrainUpdater.SetUpdateLocation(PlayerIndex, SpawnPosition.XZ, 0f, 64f);
                }
                m_terrainWaitStartTime = Time.FrameStartTime;
            }, delegate
            {
                if (Time.PeriodicEvent(0.1, 0.0)) {
                    float updateProgress2 = m_subsystemTerrain.TerrainUpdater.GetUpdateProgress(PlayerIndex, 0f, 64f);
                    UpdateSpawnDialog(null, null, 0.5f * updateProgress2, resetProgress: false);
                    if (!(updateProgress2 < 1f) || !(Time.FrameStartTime - m_terrainWaitStartTime < 15.0)) {
                        switch (m_spawnMode) {
                            case SpawnMode.InitialIntro:
                                SpawnPosition = FindIntroSpawnPosition(SpawnPosition.XZ);
                                break;
                            case SpawnMode.InitialNoIntro:
                                SpawnPosition = FindNoIntroSpawnPosition(SpawnPosition, respawn: false);
                                break;
                            case SpawnMode.Respawn:
                                SpawnPosition = FindNoIntroSpawnPosition(SpawnPosition, respawn: true);
                                break;
                            default:
                                throw new InvalidOperationException(LanguageControl.Get(fName, 5));
                        }
                        m_stateMachine.TransitionTo("WaitForTerrain");
                    }
                }
            }, null);
            m_stateMachine.AddState("WaitForTerrain", delegate
            {
                m_terrainWaitStartTime = Time.FrameStartTime;
                Vector2 center = (ComponentPlayer != null) ? ComponentPlayer.ComponentBody.Position.XZ : SpawnPosition.XZ;
                if (ClientCachePosition.HasValue) {
                    center = ClientCachePosition.Value.XZ;
                    ClientCachePosition = null;
                }
                //说明，客户端非主玩家不执行UpdateLocation
                if (!(CommonLib.WorkType == WorkType.Client && !IsMainPlayer)) m_subsystemTerrain.TerrainUpdater.SetUpdateLocation(PlayerIndex, center, MathUtils.Min(SettingsManager.VisibilityRange, 32f), 0f);
            }, delegate
            {
                if (Time.PeriodicEvent(0.1, 0.0)) {
                    float updateProgress = m_subsystemTerrain.TerrainUpdater.GetUpdateProgress(PlayerIndex, MathUtils.Min(SettingsManager.VisibilityRange, 64f), 0f);
                    UpdateSpawnDialog(null, null, 0.5f + 0.5f * updateProgress, resetProgress: false);
                    if ((updateProgress >= 1f && Time.FrameStartTime - m_terrainWaitStartTime > 2.0) || Time.FrameStartTime - m_terrainWaitStartTime >= 15.0) {
                        if (ComponentPlayer == null || ComponentPlayer.PlayerData.PlayerGUID == Guid.Empty) {
                            if (CommonLib.WorkType != WorkType.Client) {
                                SpawnPlayer(SpawnPosition, m_spawnMode);
                            }
                            else {
                                m_stateMachine.TransitionTo("WaitForPlayerEntity");
                                return;
                            }
                        }
                        m_stateMachine.TransitionTo("Playing");
                    }
                }
            }, null);
            //等待服务器返回玩家数据
            m_stateMachine.AddState("WaitForPlayerEntity", null, delegate
            {
                UpdateSpawnDialog("等待服务器响应玩家实体", null, 0f, false);
                if (Time.PeriodicEvent(0.1, 0.0)) {
                    if (ComponentPlayer != null) {
                        m_stateMachine.TransitionTo("Playing");
                    }
                }
            }, null);
            m_stateMachine.AddState("Playing", delegate
            {
                HideSpawnDialog();
                if (CommonLib.WorkType == WorkType.Client) {
                    CommonLib.Net.QueuePackage(new ClientPackage(CommonLib.Net.Self.ID, ClientState.Playing));
                }
            }, delegate
            {
                if (ComponentPlayer == null) {
                    m_stateMachine.TransitionTo("PrepareSpawn");
                }
                else if (m_playerDeathTime.HasValue) {
                    m_stateMachine.TransitionTo("PlayerDead");
                }
                else if (ComponentPlayer.ComponentHealth.Health <= 0f) {
                    m_playerDeathTime = Time.RealTime;
                }
                //服务器更新客户端玩家的区域
                if (!IsMainPlayer && CommonLib.WorkType != WorkType.Client) {
                    if (!isSetStatus) {
                        if (Client != null && Client.State != ClientState.Playing) {
                            ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                        }
                        else {
                            isSetStatus = true;
                            if (m_subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                        }
                    }
                }
            }, null);
            m_stateMachine.AddState("PlayerDead", delegate
            {
                isSetStatus = false;
                ModsManager.HookAction("OnPlayerDead", modLoader =>
                {
                    modLoader.OnPlayerDead(this);
                    return false;
                });
            }, delegate
            {
                //死掉后也继续更新
                if (CommonLib.WorkType != WorkType.Client && !IsMainPlayer) {
                    Vector2 center = (ComponentPlayer != null) ? ComponentPlayer.ComponentBody.Position.XZ : SpawnPosition.XZ;
                }
                if (CommonLib.WorkType == WorkType.Client && !IsMainPlayer) {
                    NetRestart();
                    m_stateMachine.TransitionTo("PrepareSpawn");
                }
                else if (ComponentPlayer == null) {
                    if (IsDead && readyToRestart) {
                        IsDead = false;
                        readyToRestart = false;
                        m_stateMachine.TransitionTo("PrepareSpawn");
                    }
                }
                else if (IsMainPlayer && Time.RealTime - m_playerDeathTime.Value > 1.5 && !DialogsManager.HasDialogs(ComponentPlayer.GuiWidget) && ComponentPlayer.GameWidget.Input.Any || readyToRestart) {
                    if (CommonLib.WorkType == WorkType.Client && IsMainPlayer) {
                        CommonLib.Net.QueuePackage(new NetWork.Packages.ComponentPlayerPackage(this, NetWork.Packages.ComponentPlayerPackage.PlayerAction.Restart));
                    }
                    IsDead = true;
                    readyToRestart = true;
                    NetRestart();
                }
            }, null);
        }

        public void NetRestart() {
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Cruel) {
                //禁用更新，防止一直出现dialog
                m_stateMachine.TransitionTo(null);
                DialogsManager.ShowDialog(ComponentPlayer.GuiWidget, new MessageDialog("提示", "你已在残酷模式死亡，不可复活！", LanguageControl.Yes, LanguageControl.No, btn =>
                {
                    CommonLib.Net.StopImmediate();
                }));
            }
            else if (!m_subsystemGameInfo.WorldSettings.IsAdventureRespawnAllowed) {
                //禁用更新，防止一直出现dialog
                m_stateMachine.TransitionTo(null);
                DialogsManager.ShowDialog(ComponentPlayer.GuiWidget, new MessageDialog("提示", "服务器禁止了玩家重生！", LanguageControl.Yes, LanguageControl.No, btn =>
                {
                    CommonLib.Net.StopImmediate();
                }));
            }
            else if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Adventure && !m_subsystemGameInfo.WorldSettings.IsAdventureRespawnAllowed) {
                ScreensManager.SwitchScreen("GameLoading", GameManager.WorldInfo, "AdventureRestart");
            }
            else {
                m_project.RemoveEntity(ComponentPlayer.Entity, disposeEntity: true);
            }
        }

        public void Dispose() {
            HideSpawnDialog();
        }

        public void RandomizeCharacterSkin() {
            Random random = new();
            CharacterSkinsManager.UpdateCharacterSkinsList();
            string[] array = CharacterSkinsManager.CharacterSkinsNames
                .Where(n => CharacterSkinsManager.IsBuiltIn(n) && CharacterSkinsManager.GetPlayerClass(n) == m_playerClass)
                .ToArray();
            string[] second = SubsystemPlayers.PlayersData.Select(pd => pd.CharacterSkinName).ToArray();
            string[] array2 = array.Except(second).ToArray();
            CharacterSkinName = array2.Length != 0 ? array2[random.Int(0, array2.Length - 1)] : array[random.Int(0, array.Length - 1)];
        }

        public void ResetName() {
            m_name = CharacterSkinsManager.GetDisplayName(CharacterSkinName);
            IsDefaultName = true;
        }

        public static bool VerifyName(string name) => name.Length >= 1;

        public virtual void Update() {
            m_stateMachine.Update();
        }

        public void Load(ValuesDictionary valuesDictionary) {
            SpawnPosition = valuesDictionary.GetValue("SpawnPosition", Vector3.Zero);
            FirstSpawnTime = valuesDictionary.GetValue("FirstSpawnTime", 0.0);
            LastSpawnTime = valuesDictionary.GetValue("LastSpawnTime", 0.0);
            SpawnsCount = valuesDictionary.GetValue("SpawnsCount", 0);
            Name = valuesDictionary.GetValue("Name", "Walter");
            PlayerGUID = valuesDictionary.GetValue("PlayerGUID", CommonLib.Net.Self.PlayerGuid);
            PlayerClass = valuesDictionary.GetValue("PlayerClass", PlayerClass.Male);
            Level = valuesDictionary.GetValue("Level", 1f);
            CharacterSkinName = valuesDictionary.GetValue("CharacterSkinName", CharacterSkinsManager.CharacterSkinsNames[0]);
            InputDevice = valuesDictionary.GetValue("InputDevice", InputDevice);
            IsMainPlayer = PlayerGUID == CommonLib.Net.Self.PlayerGuid;
            PlayerGameMode = valuesDictionary.GetValue("PlayerGameMode", GameMode.Follow);
        }

        public void Save(ValuesDictionary valuesDictionary) {
            valuesDictionary.SetValue("SpawnPosition", SpawnPosition);
            valuesDictionary.SetValue("FirstSpawnTime", FirstSpawnTime);
            valuesDictionary.SetValue("LastSpawnTime", LastSpawnTime);
            valuesDictionary.SetValue("SpawnsCount", SpawnsCount);
            valuesDictionary.SetValue("Name", Name);
            valuesDictionary.SetValue("PlayerGUID", PlayerGUID);
            valuesDictionary.SetValue("PlayerClass", PlayerClass);
            valuesDictionary.SetValue("Level", Level);
            valuesDictionary.SetValue("CharacterSkinName", CharacterSkinName);
            valuesDictionary.SetValue("InputDevice", InputDevice);
            if (PlayerGameMode != GameMode.Follow) {
                valuesDictionary.SetValue("PlayerGameMode", PlayerGameMode);
            }
        }

        public void WaitEntityAdded() {
            m_stateMachine.TransitionTo("WaitForPlayerEntity");
        }

        public void OnEntityAdded(Entity entity) {
            ComponentPlayer componentPlayer = entity.FindComponent<ComponentPlayer>();
            if (componentPlayer != null && componentPlayer.PlayerData == this) {
                //多维世界mod会提前为PlayerData.ComponentPlayer赋值，原版行为会在SubsystemPlayers.OnEntityAdded再次赋值
                //为阻止再次赋值时引发异常所以加了条ComponentPlayer != componentPlayer判断
                if (ComponentPlayer != null && ComponentPlayer != componentPlayer) {
                    throw new InvalidOperationException(string.Format(LanguageControl.Get(fName, 10), PlayerIndex));
                }
                ComponentPlayer = componentPlayer;
                GameWidget.ActiveCamera = GameWidget.FindCamera<FppCamera>();
                GameWidget.Target = componentPlayer;
                if (FirstSpawnTime < 0.0) {
                    FirstSpawnTime = m_subsystemGameInfo.TotalElapsedGameTime;
                }
            }
        }

        public void OnEntityRemoved(Entity entity) { }

        public Vector3 FindIntroSpawnPosition(Vector2 desiredSpawnPosition) {
            Vector2 vector = Vector2.Zero;
            float num = float.MinValue;
            for (int i = -30; i <= 30; i += 2) {
                for (int j = -30; j <= 30; j += 2) {
                    int num2 = Terrain.ToCell(desiredSpawnPosition.X) + i;
                    int num3 = Terrain.ToCell(desiredSpawnPosition.Y) + j;
                    float num4 = ScoreIntroSpawnPosition(desiredSpawnPosition, num2, num3);
                    if (num4 > num) {
                        num = num4;
                        vector = new Vector2(num2, num3);
                    }
                }
            }
            float num5 = m_subsystemTerrain.Terrain.CalculateTopmostCellHeight(Terrain.ToCell(vector.X), Terrain.ToCell(vector.Y)) + 1;
            return new Vector3(vector.X + 0.5f, num5 + 0.01f, vector.Y + 0.5f);
        }

        public Vector3 FindNoIntroSpawnPosition(Vector3 desiredSpawnPosition, bool respawn) {
            Vector3 vector = Vector3.Zero;
            float num = float.MinValue;
            for (int i = -8; i <= 8; i++) {
                for (int j = -8; j <= 8; j++) {
                    for (int k = -8; k <= 8; k++) {
                        int num2 = Terrain.ToCell(desiredSpawnPosition.X) + i;
                        int num3 = Terrain.ToCell(desiredSpawnPosition.Y) + j;
                        int num4 = Terrain.ToCell(desiredSpawnPosition.Z) + k;
                        float num5 = ScoreNoIntroSpawnPosition(desiredSpawnPosition, num2, num3, num4);
                        if (num5 > num) {
                            num = num5;
                            vector = new Vector3(num2, num3, num4);
                        }
                    }
                }
            }
            return new Vector3(vector.X + 0.5f, vector.Y + 0.01f, vector.Z + 0.5f);
        }

        public float ScoreIntroSpawnPosition(Vector2 desiredSpawnPosition, int x, int z) {
            float num = -0.01f * Vector2.Distance(new Vector2(x, z), desiredSpawnPosition);
            int num2 = m_subsystemTerrain.Terrain.CalculateTopmostCellHeight(x, z);
            if (num2 < 64
                || num2 > 85) {
                num -= 5f;
            }
            if (m_subsystemTerrain.Terrain.GetSeasonalTemperature(x, z) < 8) {
                num -= 5f;
            }
            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(x, num2, z);
            if (BlocksManager.Blocks[Terrain.ExtractContents(cellValue)].IsTransparent_(cellValue)) {
                num -= 5f;
            }
            for (int i = x - 1; i <= x + 1; i++) {
                for (int j = z - 1; j <= z + 1; j++) {
                    if (m_subsystemTerrain.Terrain.GetCellContents(i, num2 + 2, j) != 0) {
                        num -= 1f;
                    }
                }
            }
            Vector2 vector = ComponentIntro.FindOceanDirection(m_subsystemTerrain.TerrainContentsGenerator, new Vector2(x, z));
            Vector3 vector2 = new(x, num2 + 1.5f, z);
            for (int k = -1; k <= 1; k++) {
                Vector3 end = vector2 + new Vector3(30f * vector.X, 5f * k, 30f * vector.Y);
                TerrainRaycastResult? terrainRaycastResult = m_subsystemTerrain.Raycast(
                    vector2,
                    end,
                    false,
                    true,
                    (value, _) => Terrain.ExtractContents(value) != 0
                );
                if (terrainRaycastResult.HasValue) {
                    CellFace cellFace = terrainRaycastResult.Value.CellFace;
                    int cellContents2 = m_subsystemTerrain.Terrain.GetCellContents(cellFace.X, cellFace.Y, cellFace.Z);
                    if (cellContents2 != 18
                        && cellContents2 != 0) {
                        num -= 2f;
                    }
                }
            }
            return num;
        }

        public float ScoreNoIntroSpawnPosition(Vector3 desiredSpawnPosition, int x, int y, int z) {
            float num = -0.01f * Vector3.Distance(new Vector3(x, y, z), desiredSpawnPosition);
            if (y < 1
                || y >= 255) {
                num -= 100f;
            }
            int objvalue = m_subsystemTerrain.Terrain.GetCellValue(x, y - 1, z);
            int blockvalue = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            int block2value = m_subsystemTerrain.Terrain.GetCellValue(x, y + 1, z);
            Block obj = BlocksManager.Blocks[Terrain.ExtractContents(objvalue)];
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(blockvalue)];
            Block block2 = BlocksManager.Blocks[Terrain.ExtractContents(block2value)];
            if (obj.IsTransparent_(objvalue)) {
                num -= 10f;
            }
            if (!obj.IsCollidable_(objvalue)) {
                num -= 10f;
            }
            if (block.IsCollidable_(blockvalue)) {
                num -= 10f;
            }
            if (block2.IsCollidable_(block2value)) {
                num -= 10f;
            }
            foreach (PlayerData playersDatum in SubsystemPlayers.PlayersData) {
                if (playersDatum != this
                    && Vector3.DistanceSquared(playersDatum.SpawnPosition, new Vector3(x, y, z)) < MathUtils.Sqr(2)) {
                    num -= 1f;
                }
            }
            return num;
        }

        public bool CheckIsPointInWater(Point3 p) {
            for (int i = p.X - 1; i < p.X + 1; i++) {
                for (int j = p.Z - 1; j < p.Z + 1; j++) {
                    for (int num = p.Y; num > 0; num--) {
                        int cellValue = m_subsystemTerrain.Terrain.GetCellValue(p.X, num, p.Z);
                        Block block = BlocksManager.Blocks[Terrain.ExtractContents(cellValue)];
                        if (block.IsCollidable_(cellValue)) {
                            return false;
                        }
                        if (block is WaterBlock) {
                            break;
                        }
                    }
                }
            }
            return true;
        }

        public void SpawnPlayer(Vector3 position, SpawnMode spawnMode) {
            if (LastDeadPlayer != null) {
                m_project.RemoveEntity(LastDeadPlayer, false);
            }
            m_playerDeathTime = null;
            ComponentMount componentMount = null;
            if (spawnMode != SpawnMode.Respawn
                && CheckIsPointInWater(Terrain.ToCell(position))) {
                Entity entity = DatabaseManager.CreateEntity(m_project, "Boat", true);
                entity.FindComponent<ComponentBody>(true).Position = position;
                entity.FindComponent<ComponentBody>(true).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtils.DegToRad(45f));
                componentMount = entity.FindComponent<ComponentMount>(true);
                m_project.AddEntity(entity);
                position.Y += entity.FindComponent<ComponentBody>(true).BoxSize.Y;
            }
            string value = "";
            string value2 = "";
            string value3 = "";
            string value4 = "";
            int value5 = 0;
            if (spawnMode != SpawnMode.Respawn) {
                if (PlayerClass == PlayerClass.Female) {
                    if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                        && CharacterSkinName.Contains("2")) {
                        value = "";
                        value2 = MakeClothingValue(37, 2);
                        value3 = MakeClothingValue(16, 14);
                        value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(27, 0)}";
                    }
                    else if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                        && CharacterSkinName.Contains("3")) {
                        value = MakeClothingValue(31, 0);
                        value2 = $"{MakeClothingValue(13, 7)};{MakeClothingValue(5, 0)}";
                        value3 = MakeClothingValue(17, 15);
                        value4 = MakeClothingValue(29, 0);
                    }
                    else if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                        && CharacterSkinName.Contains("4")) {
                        value = MakeClothingValue(30, 7);
                        value2 = MakeClothingValue(14, 6);
                        value3 = MakeClothingValue(25, 7);
                        value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(8, 0)}";
                    }
                    else {
                        value = MakeClothingValue(30, 12);
                        value2 = $"{MakeClothingValue(37, 3)};{MakeClothingValue(1, 3)}";
                        value3 = MakeClothingValue(0, 12);
                        value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(29, 0)}";
                    }
                }
                else if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                    && CharacterSkinName.Contains("2")) {
                    value = "";
                    value2 = $"{MakeClothingValue(13, 0)};{MakeClothingValue(5, 0)}";
                    value3 = MakeClothingValue(25, 8);
                    value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(29, 0)}";
                }
                else if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                    && CharacterSkinName.Contains("3")) {
                    value = MakeClothingValue(32, 0);
                    value2 = MakeClothingValue(37, 5);
                    value3 = MakeClothingValue(0, 15);
                    value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(8, 0)}";
                }
                else if (CharacterSkinsManager.IsBuiltIn(CharacterSkinName)
                    && CharacterSkinName.Contains("4")) {
                    value = MakeClothingValue(31, 0);
                    value2 = MakeClothingValue(15, 14);
                    value3 = MakeClothingValue(0, 0);
                    value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(8, 0)}";
                }
                else {
                    value = MakeClothingValue(32, 0);
                    value2 = $"{MakeClothingValue(37, 0)};{MakeClothingValue(1, 9)}";
                    value3 = MakeClothingValue(0, 12);
                    value4 = $"{MakeClothingValue(26, 6)};{MakeClothingValue(29, 0)}";
                }
                value5 = m_subsystemGameInfo.WorldSettings.GameMode <= GameMode.Survival ? 1 : 0;
            }
            ValuesDictionary overrides = new() {
                { "Player",new ValuesDictionary { { "PlayerIndex", PlayerIndex }, { "PlayerGuid", PlayerGUID  } } },
                { "Intro", new ValuesDictionary { { "PlayIntro", spawnMode == SpawnMode.InitialIntro } } }, {
                    "Clothing",
                    new ValuesDictionary {
                        { "Clothes", new ValuesDictionary { { "Feet", value4 }, { "Legs", value3 }, { "Torso", value2 }, { "Head", value } } }
                    }
                }, {
                    "Inventory",
                    new ValuesDictionary {
                        { "Slots", new ValuesDictionary { { "Slot1", new ValuesDictionary { { "Contents", 162 }, { "Count", value5 } } } } }
                    }
                }
            };
            Vector2 v = ComponentIntro.FindOceanDirection(m_subsystemTerrain.TerrainContentsGenerator, position.XZ);
            string entityTemplateName = PlayerClass == PlayerClass.Male ? "MalePlayer" : "FemalePlayer";
            Entity entity2 = DatabaseManager.CreateEntity(m_project, entityTemplateName, overrides, true);
            entity2.FindComponent<ComponentBody>(true).Position = position;
            entity2.FindComponent<ComponentBody>(true).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Vector2.Angle(v, -Vector2.UnitY));
            m_project.AddEntity(entity2);
            if (componentMount != null) {
                entity2.FindComponent<ComponentRider>(true).StartMounting(componentMount);
            }
            LastSpawnTime = m_subsystemGameInfo.TotalElapsedGameTime;
            ++SpawnsCount;
            ModsManager.HookAction(
                "OnPlayerSpawned",
                modLoader => {
                    modLoader.OnPlayerSpawned(spawnMode, entity2.FindComponent<ComponentPlayer>(), position);
                    return false;
                }
            );
            LastDeadPlayer = null;
        }

        public string GetEntityTemplateName() {
            if (PlayerClass != 0) {
                return "FemalePlayer";
            }
            return "MalePlayer";
        }

        public virtual void UpdateSpawnDialog(string largeMessage, string smallMessage, float progress, bool resetProgress) {
            if (resetProgress) {
                m_progress = 0f;
            }
            m_progress = MathUtils.Max(progress, m_progress);
            if (m_spawnDialog == null) {
                m_spawnDialog = new SpawnDialog();
                DialogsManager.ShowDialog(GameWidget.GuiWidget, m_spawnDialog);
            }
            m_spawnDialog.TimeOfYear = m_subsystemGameInfo.WorldSettings.TimeOfYear;
            if (largeMessage != null) {
                m_spawnDialog.LargeMessage = largeMessage;
            }
            if (smallMessage != null) {
                m_spawnDialog.SmallMessage = smallMessage;
            }
            m_spawnDialog.Progress = m_progress;
        }

        public void HideSpawnDialog() {
            if (m_spawnDialog != null) {
                DialogsManager.HideDialog(m_spawnDialog);
                m_spawnDialog = null;
            }
        }

        public static string MakeClothingValue(int index, int color) => Terrain
            .MakeBlockValue(203, 0, ClothingBlock.SetClothingIndex(ClothingBlock.SetClothingColor(0, color), index))
            .ToString(CultureInfo.InvariantCulture);

        private static readonly Regex ValidNameRegex = new Regex(@"^[\u4E00-\u9FA5A-Za-z0-9_-]+$", RegexOptions.Compiled);

        public static string SanitizeName(string input) {
            if (string.IsNullOrEmpty(input)) return input;

            // 删除所有非法字符
            var sanitized = new StringBuilder();
            foreach (char c in input) {
                if (ValidNameRegex.IsMatch(c.ToString())) {
                    sanitized.Append(c);
                }
            }

            // 处理空值情况
            return sanitized.Length > 0 ? sanitized.ToString() : string.Empty;
        }

        public static string EnsureGuidSuffix(string baseName, Guid guid) {
            if (string.IsNullOrEmpty(baseName)) return baseName;
            string g = guid.ToString("N");
            string last4 = g.Substring(g.Length - 4).ToUpperInvariant();
            var m = Regex.Match(baseName, @"_[A-Fa-f0-9]{4}$");
            if (m.Success) {
                return Regex.Replace(baseName, @"_[A-Fa-f0-9]{4}$", "_" + last4);
            }
            return baseName + "_" + last4;
        }

        public static bool IsDuplicateName(string name) {
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>(throwOnError: true);
            return subsystemPlayers.PlayersData.Any(playerData => playerData.Name == name);
        }
    }
}