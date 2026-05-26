using Engine;

using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemPlayers : Subsystem, IUpdateable {
        public SubsystemTime m_subsystemTime;

        public List<PlayerData> m_playersData = [];

        public List<ComponentPlayer> m_componentPlayers = [];

        public int m_nextPlayerIndex;

        public ReadOnlyList<PlayerData> PlayersData => new(m_playersData);

        public ReadOnlyList<ComponentPlayer> ComponentPlayers => new(m_componentPlayers);

        public bool ShouldEnterCreate;

        public Vector3 GlobalSpawnPosition { get; set; }

        public UpdateOrder UpdateOrder => UpdateOrder.SubsystemPlayers;

        public event Action<PlayerData> PlayerAdded;

        public event Action<PlayerData> PlayerRemoved;

        public static Action<PlayerData> PlayerFirstUpdated;

        private PlayerData m_mainPlayer;

        private Dictionary<int, PlayerData> usedIndies = new Dictionary<int, PlayerData>();

        public Dictionary<string, string> BlackPlayerGuidList = new Dictionary<string, string>();

        public List<string> NoMsgPlayerGuidList = new List<string>();

        private Dictionary<Guid, EntityData> m_offlinePlayerEntities = new Dictionary<Guid, EntityData>();

        public PlayerData MainPlayerData {
            get {
                if (m_mainPlayer == null) {
                    m_mainPlayer = PlayersData.FirstOrDefault(p => p.IsMainPlayer);
                }
                return m_mainPlayer;
            }
        }

        public SubsystemTerrain m_subsystemTerrain;

        public SubsystemGameInfo m_subsystemGameInfo;


        public Func<Guid, bool> IsClientOnline;

        public ComponentPlayer MainPlayer => MainPlayerData?.ComponentPlayer;

        public string PlayerEntitiesDir = string.Empty;

        public bool IsPlayer(Entity entity) {
            foreach (ComponentPlayer componentPlayer in m_componentPlayers) {
                if (entity == componentPlayer.Entity) {
                    return true;
                }
            }
            return false;
        }

        public ComponentPlayer FindNearestPlayer(Vector3 position) {
            ComponentPlayer result = null;
            float num = float.MaxValue;
            foreach (ComponentPlayer componentPlayer in ComponentPlayers) {
                float num2 = Vector3.DistanceSquared(componentPlayer.ComponentBody.Position, position);
                if (num2 < num) {
                    num = num2;
                    result = componentPlayer;
                }
            }
            return result;
        }

        public void FindUnusedIndex(PlayerData playerData) {
            for (int i = 0; i < int.MaxValue; i++) {
                if (!usedIndies.ContainsKey(i)) {
                    usedIndies.Add(i, playerData);
                    playerData.PlayerIndex = i;
                    return;
                }
            }
        }

        public void AddPlayerData(PlayerData playerData) {
            if (m_playersData.Contains(playerData) || m_playersData.Any(pd => pd.NetworkGuid == playerData.NetworkGuid)) {
                throw new InvalidOperationException("Player already added.");
            }

            m_playersData.Add(playerData);

            FindUnusedIndex(playerData);

            PlayerAdded?.Invoke(playerData);

            if (playerData.m_stateMachine.CurrentState == null) {
                playerData.m_stateMachine.TransitionTo("FirstUpdate");
                PlayerFirstUpdated?.Invoke(playerData);
            }

            m_subsystemTerrain?.TerrainUpdater?.SetLastChunksUpdateCenter(-1, null);
        }

        public void RemovePlayerData(PlayerData playerData, bool disposeEntity = true) {
            if (!m_playersData.Contains(playerData)) {
                throw new InvalidOperationException("Player does not exist.");
            }
            m_playersData.Remove(playerData);
            if (usedIndies.ContainsKey(playerData.PlayerIndex)) {
                usedIndies.Remove(playerData.PlayerIndex);
            }
            if (playerData.ComponentPlayer != null) {
                try {
                    Project.RemoveEntity(playerData.ComponentPlayer.Entity, disposeEntity);
                }
                catch {
                }
            }
            PlayerRemoved?.Invoke(playerData);
            playerData.Dispose();
            m_subsystemTerrain?.TerrainUpdater?.SetLastChunksUpdateCenter(-1, null);
        }

        List<PlayerData> toRemove = new List<PlayerData>();

        public virtual void Update(float dt) {
            if (m_playersData.Count == 0 || m_playersData.All(p => !p.IsMainPlayer)) {
                ScreensManager.SwitchScreen("Player", PlayerScreen.Mode.Initial, Project);
            }
            foreach (PlayerData playersDatum in m_playersData) {
                playersDatum.Update();
            }
            foreach (PlayerData playerData in toRemove) {
                MakePlayerOffline(playerData.NetworkGuid);
            }
            toRemove.Clear();
        }

        public override void Dispose() {
            foreach (PlayerData playersDatum in m_playersData) {
                playersDatum.Dispose();
            }
        }

        public override void Load(ValuesDictionary valuesDictionary) {
            m_subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>();
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>();
            m_subsystemTime = Project.FindSubsystem<SubsystemTime>(true);
            m_nextPlayerIndex = valuesDictionary.GetValue<int>("NextPlayerIndex");
            GlobalSpawnPosition = valuesDictionary.GetValue<Vector3>("GlobalSpawnPosition");
            var blackPlayers = valuesDictionary.GetValue("BlackPlayerGuidList", new ValuesDictionary());
            foreach (var item in blackPlayers) {
                BlackPlayerGuidList[item.Key] = (string)item.Value;
            }
            var noMsgPlayers = valuesDictionary.GetValue("NoMsgPlayerGuidList", new ValuesDictionary());
            foreach (string item in noMsgPlayers.Values) {
                NoMsgPlayerGuidList.Add(item);
            }
            foreach (ValuesDictionary item in valuesDictionary.GetValue("Players", new ValuesDictionary()).Values) {
                var playerData = new PlayerData(Project);
                playerData.Load(item);
                AddPlayerData(playerData);
            }

            foreach (KeyValuePair<string, object> item in valuesDictionary.GetValue("OfflinePlayerEntities", new ValuesDictionary())) {
                if (Guid.TryParseExact(item.Key, "N", out Guid res)) {
                    var entityData = new EntityData(Project.GameDatabase, (ValuesDictionary)item.Value);
                    m_offlinePlayerEntities.Add(res, entityData);
                }
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            var OnlinePlayersListVD = new ValuesDictionary();
            int num = 0;
            foreach (PlayerData playersDatum in m_playersData) {
                var valuesDictionary3 = new ValuesDictionary();
                playersDatum.Save(valuesDictionary3);
                OnlinePlayersListVD.SetValue(num++.ToString(), valuesDictionary3);
            }
            var blackPlayers = new ValuesDictionary();
            foreach (var bpgl in BlackPlayerGuidList) {
                blackPlayers.SetValue(bpgl.Key, bpgl.Value);
            }
            valuesDictionary.SetValue("BlackPlayerGuidList", blackPlayers);
            var noMsgPlayers = new ValuesDictionary();
            for (int i = 0; i < NoMsgPlayerGuidList.Count; i++) {
                noMsgPlayers.SetValue(i.ToString(), NoMsgPlayerGuidList[i]);
            }
            valuesDictionary.SetValue("NoMsgPlayerGuidList", noMsgPlayers);
            valuesDictionary.SetValue("GlobalSpawnPosition", GlobalSpawnPosition);
            valuesDictionary.SetValue("Players", OnlinePlayersListVD);
            var offlines = new ValuesDictionary();
            valuesDictionary.SetValue("OfflinePlayerEntities", offlines);
        }

        public override void OnEntityAdded(Entity entity) {
            foreach (PlayerData playersDatum in m_playersData) {
                playersDatum.OnEntityAdded(entity);
            }
            UpdateComponentPlayers();
        }

        public override void OnEntityRemoved(Entity entity) {
            foreach (PlayerData playersDatum in m_playersData) {
                playersDatum.OnEntityRemoved(entity);
            }
            UpdateComponentPlayers();
        }

        public void UpdateComponentPlayers() {
            m_componentPlayers.Clear();
            foreach (PlayerData playersDatum in m_playersData) {
                if (playersDatum.ComponentPlayer != null) {
                    m_componentPlayers.Add(playersDatum.ComponentPlayer);
                }
            }
        }

        public void MakePlayerOffline(Guid NetworkGuid, bool showMsg = true) {
            var pd = m_playersData.Find(p => p.NetworkGuid == NetworkGuid);
            if (pd != null) {
                var updater = m_subsystemTerrain.TerrainUpdater;
                if (updater != null && updater.waitChunkList != null && updater.waitChunkList.ContainsKey(pd.ComponentPlayer)) {
                    updater.waitChunkList.Remove(pd.ComponentPlayer);
                }
                var componentPlayer = pd.ComponentPlayer;
                if (componentPlayer == null && m_offlinePlayerEntities.TryGetValue(NetworkGuid, out var entityDataPlayer)) {
                    var list = new EntityDataList { EntitiesData = new List<EntityData>(1) };
                    list.EntitiesData.Add(entityDataPlayer);
                    m_offlinePlayerEntities.Remove(NetworkGuid);
                    List<Entity> el = GameManager.Project.InitializeAndLoadEntities(list);
                    GameManager.Project.AttachEntities(el, true);
                    Entity e = el[0];
                    componentPlayer = e.FindComponent<ComponentPlayer>();
                }
                if (componentPlayer != null) {
                    Project.RemoveEntity(componentPlayer.Entity, true);
                    if (!NetworkManager.IsNetworkMode) {
                        componentPlayer.SavePlayerEntity();
                    }
                }
                m_subsystemTerrain.TerrainUpdater.RemoveUpdateLocation(pd.PlayerIndex);
                RemovePlayerData(pd, true);
            }
        }

        public bool CheckPlayerDataExists(Guid NetworkGuid, out string entityFilePath) {
            var entityFileDir = Storage.CombinePaths(m_subsystemGameInfo.DirectoryName, $"PlayerEntities/");
            entityFilePath = Storage.CombinePaths(entityFileDir, $"{NetworkGuid}.json");
            if (!Storage.FileExists(entityFilePath)) {
                entityFilePath = Storage.CombinePaths(entityFileDir, $"{NetworkGuid}.dat");
            }
            return Storage.FileExists(entityFilePath);
        }

        public bool MakePlayerOnline(Guid NetworkGuid, out PlayerData playerData, out Entity entity) {
            if (CheckPlayerDataExists(NetworkGuid, out string entityFilePath) && !NetworkManager.IsNetworkMode) {
                playerData = new PlayerData(Project);
                ValuesDictionary dict = new ValuesDictionary();
                using (Stream s = Storage.OpenFile(entityFilePath, OpenFileMode.Read)) {
                    if (entityFilePath.EndsWith(".json")) {
                        StreamReader reader = new StreamReader(s);
                        string jsonText = reader.ReadToEnd();
                        reader.Dispose();
                        dict.ApplyOverridesUseJson(jsonText, out byte[] data);
                    }
                    else if (entityFilePath.EndsWith(".dat")) {
                        byte[] d = new byte[s.Length];
                        s.Read(d, 0, d.Length);
                        dict.ApplyOverridesUseMessagePack(d);
                        Storage.DeleteFile(entityFilePath);
                    }
                }
                var entityData = new EntityData(Project.GameDatabase, dict.GetValue<ValuesDictionary>("Entity"));
                playerData.Load(dict.GetValue<ValuesDictionary>("Data"));
                AddPlayerData(playerData);
                playerData.WaitEntityAdded();
                var list = new EntityDataList { EntitiesData = new List<EntityData>(1) };
                list.EntitiesData.Add(entityData);
                List<Entity> el = GameManager.Project.InitializeAndLoadEntities(list);
                GameManager.Project.AttachEntities(el, true);
                if (el.Count > 0) {
                    entity = el[0];
                    entity.Id = 0;
                }
                else {
                    entity = null;
                }
                return true;
            }

            playerData = null;
            entity = null;
            return false;
        }

        public PlayerData FindPlayerData(Func<PlayerData, bool> f) {
            return m_playersData.FirstOrDefault(f);
        }

        public static bool IsMainPlayer(Entity e) {
            var player = e.FindComponent<ComponentPlayer>();
            return player != null && player.PlayerData.IsMainPlayer;
        }
    }
}


