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
        NetworkManager.ServerSessions.ToList().ForEach(MakePlayerOffline);

    public void MakePlayerOffline(ClientSession session) {
        if (session == null || session.PlayerIndex < 0) {
            return;
        }
        int index = session.PlayerIndex;
        Guid guid = session.NetworkGuid;
        var pd = m_subsystemPlayers.m_playersData.Find(p => p.PlayerIndex == index);
        if (pd != null) {
            var updater = m_subsystemTerrain.TerrainUpdater;
            updater?.RemovePlayerChunkSendState(pd.PlayerIndex);
            var componentPlayer = pd.ComponentPlayer;
            if (componentPlayer == null && m_subsystemPlayers.m_offlinePlayerEntities.TryGetValue(pd.PlayerIndex, out var entityDataPlayer)) {
                var list = new EntityDataList { EntitiesData = new List<EntityData>(1) };
                list.EntitiesData.Add(entityDataPlayer);
                m_subsystemPlayers.m_offlinePlayerEntities.Remove(pd.PlayerIndex);
                List<Entity> el = GameManager.Project.InitializeAndLoadEntities(list);
                RemoveExistingEntitiesWithSameIds(el);
                GameManager.Project.AttachEntities(el, true);
                Entity e = el[0];
                componentPlayer = e.FindComponent<ComponentPlayer>();
            }
            if (componentPlayer != null) {
                Project.RemoveEntity(componentPlayer.Entity, true);
                if (!NetworkManager.IsClientRunning) {
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

    public bool MakePlayerOnline(Guid guid, out PlayerData playerData) {
        if (CheckPlayerDataExists(guid, out string entityFilePath) && !NetworkManager.IsClientRunning) {
            playerData = new PlayerData(Project);
            ValuesDictionary dict = LoadSavedPlayerDictionary(entityFilePath);
            playerData.Load(dict.GetValue<ValuesDictionary>("Data"));
            RemoveStalePlayerData(playerData.PlayerIndex);
            m_subsystemPlayers.AddPlayerData(playerData, true);
            playerData.WaitEntityAdded();
            return true;
        }

        playerData = null;
        return false;
    }

    public bool MakePlayerEntityOnline(ClientSession session, out Entity entity) {
        entity = null;
        if (session == null
            || session.PlayerIndex < 0
            || NetworkManager.IsClientRunning
            || !CheckPlayerDataExists(session.NetworkGuid, out string entityFilePath)) {
            return false;
        }

        PlayerData playerData = session.GetPlayerData();
        if (playerData == null || playerData.ComponentPlayer != null) {
            return false;
        }

        ValuesDictionary dict = LoadSavedPlayerDictionary(entityFilePath);
        var entityData = new EntityData(Project.GameDatabase, dict.GetValue<ValuesDictionary>("Entity"));
        if (entityFilePath.EndsWith(".dat")) {
            Storage.DeleteFile(entityFilePath);
        }

        var list = new EntityDataList { EntitiesData = new List<EntityData>(1) };
        list.EntitiesData.Add(entityData);
        List<Entity> el = GameManager.Project.InitializeAndLoadEntities(list);
        if (el.Count == 0) {
            return false;
        }

        foreach (Entity loadedEntity in el) {
            loadedEntity.Id = 0;
        }
        GameManager.Project.AttachEntities(el, true);
        entity = el[0];
        return true;
    }

    public ValuesDictionary LoadSavedPlayerDictionary(string entityFilePath) {
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
            }
        }
        return dict;
    }

    public static void RemoveExistingEntitiesWithSameIds(IEnumerable<Entity> entities) {
        List<Entity> entitiesToRemove = [];
        foreach (Entity entity in entities) {
            if (entity.Id == 0) {
                continue;
            }
            Entity existingEntity = GameManager.Project.FindEntity(entity.Id);
            if (existingEntity != null) {
                entitiesToRemove.Add(existingEntity);
            }
        }
        foreach (Entity entity in entitiesToRemove) {
            GameManager.Project.RemoveEntity(entity, true);
        }
    }

    public void RemoveStalePlayerData(int playerIndex) {
        if (playerIndex < 0) {
            return;
        }
        List<PlayerData> stalePlayers = m_subsystemPlayers.m_playersData
            .Where(playerData => playerData.PlayerIndex == playerIndex
                && !playerData.IsMainPlayer
                && !NetworkManager.ServerSessions.Any(session => session.PlayerIndex == playerData.PlayerIndex))
            .ToList();
        foreach (PlayerData playerData in stalePlayers) {
            int stalePlayerIndex = playerData.PlayerIndex;
            Log.Warning($"Removing stale network player data. PlayerIndex={stalePlayerIndex}, Name={playerData.Name}");
            m_subsystemTerrain.TerrainUpdater.RemoveUpdateLocation(stalePlayerIndex);
            if (m_subsystemPlayers.m_offlinePlayerEntities.ContainsKey(stalePlayerIndex)) {
                m_subsystemPlayers.m_offlinePlayerEntities.Remove(stalePlayerIndex);
            }
            m_subsystemPlayers.RemovePlayerData(playerData, true);
        }
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
