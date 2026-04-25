using System.Xml.Linq;
using Engine;
using Game.NetWork;
using Game.NetWork.Packages;
using GameEntitySystem;
using TemplatesDatabase;
using XmlUtilities;

namespace Game {
    public static class GameManager {
        public static WorldInfo m_worldInfo;

        public static Project m_project;

        public static SubsystemUpdate m_subsystemUpdate;

        public static ManualResetEvent m_saveCompleted = new(true);

        public static Project Project => m_project;

        public static WorldInfo WorldInfo => m_worldInfo;

        public static List<Func<bool>> SyncDispatcher = [];

        public static event Action<Project> ProjectDisposed;

        public const string fName = "GameManager";

        static readonly Action<Project> m_onProjectLoadedHandler = project => {
            ModsManager.HookAction(
                "OnProjectLoaded",
                loader => {
                    loader.OnProjectLoaded(project);
                    return false;
                }
            );
        };

        static readonly EventHandler<EntityAddRemoveEventArgs> m_onEntityAddedHandler = (_, arg) => {
            ModsManager.HookAction(
                "OnEntityAdd",
                loader => {
                    loader.OnEntityAdd(arg.Entity);
                    return false;
                }
            );
        };

        static readonly EventHandler<EntityAddRemoveEventArgs> m_onEntityRemovedHandler = (_, arg) => {
            ModsManager.HookAction(
                "OnEntityRemove",
                loader => {
                    loader.OnEntityRemove(arg.Entity);
                    return false;
                }
            );
        };

        static readonly Action<Entity, List<KeyValuePair<int, Component>>> m_onEntityComponentsInitializedHandler = (entity, componentList) => {
            ModsManager.HookAction(
                "EntityComponentsInitialized",
                loader => {
                    loader.EntityComponentsInitialized(entity, componentList);
                    return false;
                }
            );
        };

        static readonly Action<Project> m_beforeSubsystemsAndEntitiesLoadHandler = project => {
            ModsManager.HookAction(
                "ProjectBeforeSubsystemsAndEntitiesLoad",
                loader => {
                    loader.ProjectBeforeSubsystemsAndEntitiesLoad(project);
                    return false;
                }
            );
        };

        static readonly EventHandler<EntityAddRemoveEventArgs> m_serverProjectEntityAddedHandler = (_, arg) => {
            foreach (Client client in NetworkManager.Clients.Values.Where(c => c != NetworkManager.Self)) {
                NetworkManager.QueuePackage(new EntityPackage(arg.Entity) { To = client });
            }
        };

        static readonly EventHandler<EntityAddRemoveEventArgs> m_serverProjectEntityRemovedHandler = (_, arg) => {
            NetworkManager.QueuePackage(new EntityPackage(arg.Entity.Id));
        };

        static Action<Client> m_clientStateChangedHandler;

        public static void RepairAndUpgradeWorld(WorldInfo worldInfo) {
            WorldsManager.RepairWorldIfNeeded(worldInfo.DirectoryName);
            VersionsManager.UpgradeWorld(worldInfo.DirectoryName);
        }

        public static void LoadProject(byte[] messagePackData, ContainerWidget gamesWidget, bool useNetProj = true) {
            DisposeProject();
            BindModProjectHooks();
            var valuesDictionary = new ValuesDictionary();
            var valuesDictionary2 = new ValuesDictionary();
            valuesDictionary.SetValue("GameInfo", valuesDictionary2);
            valuesDictionary2.SetValue("WorldDirectoryName", "NetWorld");
            var valuesDictionary3 = new ValuesDictionary();
            valuesDictionary.SetValue("Views", valuesDictionary3);
            valuesDictionary3.SetValue("GamesWidget", gamesWidget);
            var projectData = new ProjectData(DatabaseManager.GameDatabase, messagePackData, valuesDictionary, ignoreInvalidEntities: true);
            if (useNetProj) m_project = new Project(DatabaseManager.GameDatabase, projectData);
            else m_project = new Project(DatabaseManager.GameDatabase, projectData);
            HookAction();
            m_subsystemUpdate = m_project.FindSubsystem<SubsystemUpdate>(throwOnError: true);
            m_worldInfo = new WorldInfo();
            Log.Information("加载NetProject");
            GC.Collect();
        }

        public static void LoadProject(WorldInfo worldInfo, ContainerWidget gamesWidget, bool useNetProj = true) {
            DisposeProject();
            BindModProjectHooks();
            WorldsManager.RepairWorldIfNeeded(worldInfo.DirectoryName);
            VersionsManager.UpgradeWorld(worldInfo.DirectoryName);
            BlocksManager.LoadBlocksStaticly = string.IsNullOrEmpty(worldInfo.APIVersion);
            using (Stream stream = Storage.OpenFile(Storage.CombinePaths(worldInfo.DirectoryName, "Project.xml"), OpenFileMode.Read)) {
                ValuesDictionary valuesDictionary = new();
                ValuesDictionary valuesDictionary2 = new();
                valuesDictionary.SetValue("GameInfo", valuesDictionary2);
                valuesDictionary2.SetValue("WorldDirectoryName", worldInfo.DirectoryName);
                ValuesDictionary valuesDictionary3 = new();
                valuesDictionary.SetValue("Views", valuesDictionary3);
                valuesDictionary3.SetValue("GamesWidget", gamesWidget);
                XElement projectNode = XmlUtils.LoadXmlFromStream(stream, null, true);
                ModsManager.HookAction(
                    "ProjectXmlLoad",
                    loader => {
#pragma warning disable CS0618
                        loader.ProjectXmlLoad(projectNode);
#pragma warning restore CS0618
                        loader.ProjectXmlLoad(projectNode, worldInfo, gamesWidget);
                        return false;
                    }
                );
                ProjectData projectData = new(DatabaseManager.GameDatabase, projectNode, valuesDictionary, true);
                if (useNetProj) m_project = new Project(DatabaseManager.GameDatabase, projectData);
                else m_project = new Project(DatabaseManager.GameDatabase, projectData);
                HookAction();
                m_subsystemUpdate = m_project.FindSubsystem<SubsystemUpdate>(true);
            }
            m_worldInfo = worldInfo;
            Log.Information(
                LanguageControl.Get(fName, "1"),
                LanguageControl.Get("GameMode", worldInfo.WorldSettings.GameMode.ToString()),
                LanguageControl.Get("StartingPositionMode", worldInfo.WorldSettings.StartingPositionMode.ToString()),
                worldInfo.WorldSettings.Name,
                SettingsManager.VisibilityRange.ToString(),
                LanguageControl.Get("ResolutionMode", SettingsManager.ResolutionMode.ToString())
            );
            GC.Collect();
        }

        public static void SaveProject(bool waitForCompletion, bool showErrorDialog) {
            if (m_project != null && NetworkManager.WorkType == WorkType.Server) {
                double realTime = Time.RealTime;
                ProjectData projectData = m_project.Save();
                m_saveCompleted.WaitOne();
                m_saveCompleted.Reset();
                SubsystemGameInfo subsystemGameInfo = m_project.FindSubsystem<SubsystemGameInfo>(true);
                Task.Run(() => InternalSaveProject(projectData, subsystemGameInfo.DirectoryName, showErrorDialog));
                if (waitForCompletion) {
                    m_saveCompleted.WaitOne();
                }
                Log.Verbose(string.Format(LanguageControl.Get(fName, "4"), Math.Round((Time.RealTime - realTime) * 1000.0)));
            }
        }

        public static void InternalSaveProject(ProjectData projectData, string directoryName, bool showErrorDialog) {
            try {
                string projectFileName = Storage.CombinePaths(directoryName, "Project.xml");
                WorldsManager.MakeQuickWorldBackup(directoryName);
                XElement xElement = new("Project");
                ModsManager.HookAction(
                    "ProjectXmlSave",
                    loader => {
                        loader.ProjectXmlSave(xElement);
                        return false;
                    }
                );
                projectData.Save(xElement);
                XmlUtils.SetAttributeValue(xElement, "Version", VersionsManager.SerializationVersion);
                XmlUtils.SetAttributeValue(xElement, "APIVersion", ModsManager.APIVersionString);
                Storage.CreateDirectory(directoryName);
                ModsManager.HookAction(
                    "OnProjectXmlSaved",
                    loader => {
                        loader.OnProjectXmlSaved(xElement);
                        return false;
                    }
                );
                using (Stream stream = Storage.OpenFile(projectFileName, OpenFileMode.Create)) {
                    XmlUtils.SaveXmlToStream(xElement, stream, null, true);
                }
            }
            catch (Exception ex) {
                if (showErrorDialog) {
                    Dispatcher.Dispatch(
                        delegate {
                            DialogsManager.ShowDialog(
                                null,
                                new MessageDialog(
                                    LanguageControl.Get(fName, "2"),
                                    $"{ex.Message}\n{LanguageControl.Get(fName, "3")}",
                                    LanguageControl.Ok,
                                    null,
                                    null
                                )
                            );
                        }
                    );
                }
                Log.Error($"{LanguageControl.Get(fName, "2")}\n{ex}");
            }
            finally {
                m_saveCompleted.Set();
            }
        }

        public static void UpdateProject() {
            if (SyncDispatcher.Count > 0
                && SyncDispatcher[0].Invoke()) {
                SyncDispatcher.RemoveAt(0);
            }
            if (m_project != null) {
                m_subsystemUpdate.Update();
            }
        }

        public static void DisposeProject() {
            if (m_project != null) {
                UnbindRuntimeHooks();
                ProjectDisposed?.Invoke(m_project);
                m_project.Dispose();
                m_project = null;
                m_subsystemUpdate = null;
                m_worldInfo = null;
                ModsManager.HookAction(
                    "OnProjectDisposed",
                    loader => {
                        loader.OnProjectDisposed();
                        return false;
                    }
                );
                GC.Collect();
            }
        }

        public static void HookAction() {
            if (m_clientStateChangedHandler != null && NetworkManager.WorkType != WorkType.Local) {
                NetworkManager.OnClientStateChanged -= m_clientStateChangedHandler;
                m_clientStateChangedHandler = null;
            }

            if (NetworkManager.WorkType == WorkType.Server) {
                Project.EntityAdded -= m_serverProjectEntityAddedHandler;
                Project.EntityRemoved -= m_serverProjectEntityRemovedHandler;
                Project.EntityAdded += m_serverProjectEntityAddedHandler;
                Project.EntityRemoved += m_serverProjectEntityRemovedHandler;
            }
            var subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>();
            var subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>();

            if (NetworkManager.WorkType == WorkType.Client) {
                m_clientStateChangedHandler = c => {
                    switch (c.State) {
                        case ClientStage.NotConnected: {
                                var playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerGUID == c.PlayerGuid);
                                if (playerData != null && !playerData.IsMainPlayer) {
                                    subsystemPlayers.MakePlayerOffline(playerData.PlayerGUID);
                                }
                                break;
                        }
                    }
                };
                NetworkManager.OnClientStateChanged += m_clientStateChangedHandler;
                SettingsManager.VisibilityRange = Math.Min(256, SettingsManager.VisibilityRange);
            }
            else if (NetworkManager.WorkType == WorkType.Server) {
                m_clientStateChangedHandler = c => {
                    if (NetworkManager.WorkType == WorkType.Server) {
                        NetworkManager.QueuePackage(new ClientPackage(c.ID, c.State) { Except = c });

                        switch (c.State) {
                            case ClientStage.Connected: {
                                    byte[] textureData = null;
                                    if (subsystemPlayers.MakePlayerOnline(c.PlayerGuid, out PlayerData playerData, out Entity entity)) {
                                        c.CachePlayerEntity = entity;
                                        foreach (var client in NetworkManager.Clients) {
                                            if (client.Value != NetworkManager.Self && client.Value != c) {
                                                NetworkManager.QueuePackage(new PlayerDataPackage(playerData, true) { To = client.Value });
                                            }
                                        }
                                    }
                                    if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                                        using (var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read)) {
                                            textureData = ModsManager.StreamToBytes(s);
                                        }
                                    }
                                    byte[] data = NetworkManager.GetNowProject(Project, c);
                                    NetworkManager.QueuePackage(new ProjectPackage(textureData, data) { To = c });
                                    break;
                                }
                            case ClientStage.NotConnected: {
                                    subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                                    GC.Collect();
                                    break;
                                }
                            case ClientStage.ProjectLoaded: {
                                    List<Entity> sendList = [.. Project.Entities];
                                    //向客户端广播实体列表
                                    NetworkManager.QueuePackage(new EntityPackage(sendList) { To = c });
                                    //如果有实体，添加到Project中并广播给其它客户端
                                    if (c.CachePlayerEntity != null) {
                                        Project.AddEntity(c.CachePlayerEntity);
                                        c.CachePlayerEntity = null;
                                    }
                                    break;
                                }
                            case ClientStage.LoadTerrain:
                                GC.Collect();
                                //进入无敌状态
                                if (c.PlayerData != null && c.PlayerData.ComponentPlayer != null) {
                                    if (Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.GameMode != GameMode.Creative) {
                                        c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                                    }
                                }
                                break;
                            case ClientStage.Playing:
                                if (c.PlayerData == null) {
                                    NetworkManager.Server.RemoveClient(c, "未知错误！该Client没有对应PlayerData");
                                }
                                else {
                                    //解除无敌状态
                                    if (c.PlayerData.ComponentPlayer != null) {
                                        if (Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.GameMode != GameMode.Creative) {
                                            c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                                        }
                                    }
                                }
                                break;
                        }
                    }
                    else {
                        switch (c.State) {
                            case ClientStage.NotConnected:
                                subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                                break;
                        }
                    }
                };
                NetworkManager.OnClientStateChanged += m_clientStateChangedHandler;
            }

            if (NetworkManager.WorkType == WorkType.Client) {
                NetworkManager.QueuePackage(new ClientPackage(NetworkManager.Self.ID, ClientStage.ProjectLoaded));
            }
            //开启包处理
            NetworkManager.TurnOnPackageHanlde();
        }

        static void BindModProjectHooks() {
            Project.OnProjectLoad -= m_onProjectLoadedHandler;
            Project.EntityAdded -= m_onEntityAddedHandler;
            Project.EntityRemoved -= m_onEntityRemovedHandler;
            Project.BeforeSubsystemsAndEntitiesLoad -= m_beforeSubsystemsAndEntitiesLoadHandler;
            Entity.EntityComponentsInitialized -= m_onEntityComponentsInitializedHandler;

            Project.OnProjectLoad += m_onProjectLoadedHandler;
            Project.EntityAdded += m_onEntityAddedHandler;
            Project.EntityRemoved += m_onEntityRemovedHandler;
            Project.BeforeSubsystemsAndEntitiesLoad += m_beforeSubsystemsAndEntitiesLoadHandler;
            Entity.EntityComponentsInitialized += m_onEntityComponentsInitializedHandler;
        }

        static void UnbindRuntimeHooks() {
            Project.EntityAdded -= m_serverProjectEntityAddedHandler;
            Project.EntityRemoved -= m_serverProjectEntityRemovedHandler;

            if (m_clientStateChangedHandler != null && NetworkManager.WorkType != WorkType.Local) {
                NetworkManager.OnClientStateChanged -= m_clientStateChangedHandler;
                m_clientStateChangedHandler = null;
            }

            Project.OnProjectLoad -= m_onProjectLoadedHandler;
            Project.EntityAdded -= m_onEntityAddedHandler;
            Project.EntityRemoved -= m_onEntityRemovedHandler;
            Project.BeforeSubsystemsAndEntitiesLoad -= m_beforeSubsystemsAndEntitiesLoadHandler;
            Entity.EntityComponentsInitialized -= m_onEntityComponentsInitializedHandler;
        }
    }
}
