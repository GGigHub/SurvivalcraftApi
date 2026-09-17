using Engine;

using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemPlayers : Subsystem, IUpdateable {
        public SubsystemTime m_subsystemTime;

        public List<PlayerData> m_playersData = [];

        public List<ComponentPlayer> m_componentPlayers = [];

        public int m_nextPlayerIndex;

        public bool PlayerStartedPlaying;

        public ReadOnlyList<PlayerData> PlayersData => new(m_playersData);

        public ReadOnlyList<ComponentPlayer> ComponentPlayers => new(m_componentPlayers);

        public bool ShouldEnterCreate;

        public Vector3 GlobalSpawnPosition { get; set; }

        public UpdateOrder UpdateOrder => UpdateOrder.SubsystemPlayers;

        public event Action<PlayerData> PlayerAdded;

        public event Action<PlayerData> PlayerRemoved;

        private PlayerData m_mainPlayer;

        private Dictionary<int, PlayerData> usedIndies = new Dictionary<int, PlayerData>();

        public Dictionary<string, string> BlackPlayerGuidList = new Dictionary<string, string>();

        public List<string> NoMsgPlayerGuidList = new List<string>();

        public Dictionary<int, EntityData> m_offlinePlayerEntities = new Dictionary<int, EntityData>();

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


        public Func<int, bool> IsClientOnline;

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
            if (NetworkManager.IsClientRunning) {
                usedIndies[playerData.PlayerIndex] = playerData;
                return;
            }

            for (int i = 0; i < int.MaxValue; i++) {
                if (usedIndies.TryAdd(i, playerData)) {
                    playerData.PlayerIndex = i;
                    return;
                }
            }
        }

        public void AddPlayerData(PlayerData playerData) {
            AddPlayerData(playerData, false);
        }

        public void AddPlayerData(PlayerData playerData, bool preservePlayerIndex) {
            if (m_playersData.Contains(playerData)) {
                throw new InvalidOperationException("Player already added.");
            }

            m_playersData.Add(playerData);

            if (preservePlayerIndex
                && !NetworkManager.IsClientRunning
                && playerData.PlayerIndex >= 0
                && !usedIndies.ContainsKey(playerData.PlayerIndex)) {
                usedIndies[playerData.PlayerIndex] = playerData;
            }
            else {
                FindUnusedIndex(playerData);
            }

            PlayerAdded?.Invoke(playerData);

            if (playerData.m_stateMachine.CurrentState == null) {
                playerData.m_stateMachine.TransitionTo("FirstUpdate");
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

        public virtual void Update(float dt) {
            if (m_playersData.Count == 0 || m_playersData.All(p => !p.IsMainPlayer)) {
                ScreensManager.SwitchScreen("Player", PlayerScreen.Mode.Initial, Project);
            }
            foreach (PlayerData playersDatum in m_playersData) {
                playersDatum.Update();
            }
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
                if (int.TryParse(item.Key, out int res)) {
                    var entityData = new EntityData(Project.GameDatabase, (ValuesDictionary)item.Value);
                    m_offlinePlayerEntities.Add(res, entityData);
                }
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            var OnlinePlayersListVD = new ValuesDictionary();
            bool isStorageServerSave = GameManager.IsSavingProjectToStorage && NetworkManager.IsServerRunning;
            Dictionary<int, ClientSession> sessionsByPlayerIndex = isStorageServerSave
                ? NetworkManager.ServerSessions
                    .Where(s => s != null && s.PlayerIndex >= 0)
                    .GroupBy(s => s.PlayerIndex)
                    .ToDictionary(g => g.Key, g => g.First())
                : null;
            int num = 0;
            foreach (PlayerData playersDatum in m_playersData) {
                if (isStorageServerSave && !playersDatum.IsMainPlayer) {
                    if (playersDatum.ComponentPlayer != null
                        && sessionsByPlayerIndex != null
                        && sessionsByPlayerIndex.TryGetValue(playersDatum.PlayerIndex, out ClientSession session)
                        && session.NetworkGuid != Guid.Empty) {
                        NetworkManager.SavePlayerEntity(playersDatum.ComponentPlayer, session.NetworkGuid);
                    }
                    continue;
                }
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

        public PlayerData FindPlayerData(Func<PlayerData, bool> f) {
            return m_playersData.FirstOrDefault(f);
        }

        public static bool IsMainPlayer(Entity e) {
            var player = e.FindComponent<ComponentPlayer>();
            return player != null && player.PlayerData.IsMainPlayer;
        }
    }
}

