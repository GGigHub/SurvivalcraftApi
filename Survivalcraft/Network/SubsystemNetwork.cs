using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class SubsystemNetwork : Subsystem {
    public SubsystemPlayers m_subsystemPlayers;
    public SubsystemTerrain m_subsystemTerrain;
    public SubsystemGameInfo m_subsystemGameInfo;

    public override void Load(ValuesDictionary valuesDictionary) {
        m_subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>(true);
        m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
        m_subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>(true);
    }

    public void MakeAllPlayersOffline() => 
        NetworkManager.NetworkServer.ClientSessions.Values.ToList().ForEach(MakePlayerOffline);

    public void MakePlayerOffline(ClientSession session) {
        if (session == null || session.PlayerIndex < 0) {
            return;
        }
        int index = session.PlayerIndex;
        Guid guid = session.NetworkGuid;
        var pd = m_subsystemPlayers.m_playersData.Find(p => p.PlayerIndex == index);
        if (pd != null) {
            var updater = m_subsystemTerrain.TerrainUpdater;
            if (updater != null && updater.waitChunkList != null && updater.waitChunkList.ContainsKey(pd.PlayerIndex)) {
                updater.waitChunkList.Remove(pd.PlayerIndex);
            }
            var componentPlayer = pd.ComponentPlayer;
            if (componentPlayer == null && m_subsystemPlayers.m_offlinePlayerEntities.TryGetValue(pd.PlayerIndex, out var entityDataPlayer)) {
                var list = new EntityDataList { EntitiesData = new List<EntityData>(1) };
                list.EntitiesData.Add(entityDataPlayer);
                m_subsystemPlayers.m_offlinePlayerEntities.Remove(pd.PlayerIndex);
                List<Entity> el = GameManager.Project.InitializeAndLoadEntities(list);
                GameManager.Project.AttachEntities(el, true);
                Entity e = el[0];
                componentPlayer = e.FindComponent<ComponentPlayer>();
            }
            if (componentPlayer != null) {
                Project.RemoveEntity(componentPlayer.Entity, true);
                if (!NetworkManager.IsNetworkMode) {
                    SavePlayerEntity(componentPlayer, guid);
                }
            }
            m_subsystemTerrain.TerrainUpdater.RemoveUpdateLocation(pd.PlayerIndex);
            m_subsystemPlayers.RemovePlayerData(pd, true);
        }
    }

    public bool CheckPlayerDataExists(Guid guid, out string entityFilePath) {
        var entityFileDir = Storage.CombinePaths(m_subsystemGameInfo.DirectoryName, $"PlayerEntities/");
        entityFilePath = Storage.CombinePaths(entityFileDir, $"{guid}.json");
        if (!Storage.FileExists(entityFilePath)) {
            entityFilePath = Storage.CombinePaths(entityFileDir, $"{guid}.dat");
        }
        return Storage.FileExists(entityFilePath);
    }

    public bool MakePlayerOnline(Guid guid, out PlayerData playerData, out Entity entity) {
        if (CheckPlayerDataExists(guid, out string entityFilePath) && !NetworkManager.IsNetworkMode) {
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
                    s.ReadExactly(d, 0, d.Length);
                    dict.ApplyOverridesUseMessagePack(d);
                    Storage.DeleteFile(entityFilePath);
                }
            }
            var entityData = new EntityData(Project.GameDatabase, dict.GetValue<ValuesDictionary>("Entity"));
            playerData.Load(dict.GetValue<ValuesDictionary>("Data"));
            m_subsystemPlayers.AddPlayerData(playerData);
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

    public virtual void SavePlayerEntity(ComponentPlayer componentPlayer, Guid guid) {
        try {
            var list = Project.SaveEntities([componentPlayer.Entity]);
            var dict = new ValuesDictionary();
            var vdict = new ValuesDictionary();
            var pdict = new ValuesDictionary();
            list.EntitiesData[0].Save(vdict);
            componentPlayer.PlayerData.Save(pdict);
            var entityFileDir = Storage.CombinePaths(m_subsystemGameInfo.DirectoryName, "PlayerEntities/");
            if (!Storage.DirectoryExists(entityFileDir)) {
                Storage.CreateDirectory(entityFileDir);
            }
            dict.SetValue("Entity", vdict);
            dict.SetValue("Data", pdict);
            using (Stream stream = Storage.OpenFile(Storage.CombinePaths(entityFileDir, $"{guid}.json"), OpenFileMode.Create)) {
                StreamWriter streamWriter = new(stream);
                streamWriter.Write(dict.ToJsonText());
                streamWriter.Flush();
                streamWriter.Dispose();
            }
        }
        catch (Exception e) {
            Log.Warning("Persist player on death failed: " + e.Message);
        }
    }
}