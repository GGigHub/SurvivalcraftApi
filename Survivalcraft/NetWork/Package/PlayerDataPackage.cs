using Engine;
using System;
using GameEntitySystem;
using TemplatesDatabase;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class PlayerDataPackage : IPackage
{
    public byte ID => (byte)PackageType.PlayerData;
    public enum DataType
    {
        Create,
        Modify,
        Delete,
        ClientKnownPlayer,
        AddPlayer,
        SetUpdateLocation,
        CloseTime,
        AddNoMsg,
        RemoveNoMsg,
        Bugle,
        Count
    }

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.Connected;
    public int m_visibility;
    public ValuesDictionary m_vd;
    public Guid m_playerGuid;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;
    public DataType m_type;
    public EntityDataList m_entity;
    public TerrainUpdater.UpdateLocation m_updateLocation;

    public string m_bugleTitle; //小喇叭标题
    public string m_bugleContent; //小喇叭内容
    public int m_playerCount; //玩家人数

    public PlayerDataPackage()
    {

    }

    public PlayerDataPackage(PlayerData playerData, bool confusion)
        : this(playerData, DataType.AddPlayer)
    {
        if (confusion)
        {
            m_vd.SetValue("PlayerGUID", playerData.Client.TokenId);
        }
    }

    public PlayerDataPackage(PlayerData playerData, DataType dataType)
    {
        m_vd = new ValuesDictionary();
        m_type = dataType;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerGuid = playerData.PlayerGUID;
        m_playerClass = playerData.m_playerClass;
        playerData.Save(m_vd);
    }

    public PlayerDataPackage(Guid guid, bool add)
    {
        m_type = add ? DataType.AddNoMsg : DataType.RemoveNoMsg;
        m_playerGuid = guid;
    }

    public PlayerDataPackage(int time, string msg)
    {
        m_playerName = msg;
        m_type = DataType.CloseTime;
        m_visibility = time;
    }

    public PlayerDataPackage(TerrainUpdater.UpdateLocation updateLocation)
    {
        m_updateLocation = updateLocation;
        m_type = DataType.SetUpdateLocation;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        byte[] messagePack = null;
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case DataType.AddPlayer:
            case DataType.Create:
                messagePack = m_vd.ToMessagePack();
                writer.WriteBuff(messagePack);
                break;
            case DataType.Modify:
                writer.Write(m_playerGuid);
                writer.Write(m_playerName);
                writer.Write(m_skinName);
                writer.WriteEnum(m_playerClass);
                break;
            case DataType.ClientKnownPlayer:
                writer.Write(m_playerGuid);
                break;
            case DataType.SetUpdateLocation:
                writer.Write(m_updateLocation.Center);
                writer.Write((ushort)m_updateLocation.ContentDistance);
                writer.Write((ushort)m_updateLocation.VisibilityDistance);
                writer.Write(m_updateLocation.LastChunksUpdateCenter);
                break;
            case DataType.CloseTime:
                writer.Write(m_visibility);
                writer.Write(m_playerName);
                break;
            case DataType.Bugle:
                writer.Write(m_bugleTitle);
                writer.Write(m_bugleContent);
                break;
            case DataType.Count:
                writer.Write(m_playerCount);
                break;
            case DataType.AddNoMsg:
            case DataType.RemoveNoMsg:
                writer.Write(m_playerGuid);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        byte[] messagePack = null;
        m_type = reader.ReadEnum<DataType>();
        switch (m_type)
        {
            case DataType.AddPlayer:
            case DataType.Create:
                messagePack = reader.ReadBuff();
                m_vd = new ValuesDictionary();
                m_vd.ApplyOverridesUseMessagePack(messagePack);
                break;
            case DataType.Modify:
                m_playerGuid = reader.ReadGuid();
                m_playerName = reader.ReadString();
                m_skinName = reader.ReadString();
                m_playerClass = reader.ReadEnum<PlayerClass>();
                break;
            case DataType.ClientKnownPlayer:
                m_playerGuid = reader.ReadGuid();
                break;
            case DataType.SetUpdateLocation:
                m_updateLocation = new TerrainUpdater.UpdateLocation();
                m_updateLocation.Center = reader.ReadVector2();
                m_updateLocation.ContentDistance = reader.ReadUInt16();
                m_updateLocation.VisibilityDistance = reader.ReadUInt16();
                m_updateLocation.LastChunksUpdateCenter = reader.ReadVector2Nullable();
                break;
            case DataType.CloseTime:
                m_visibility = reader.ReadInt32();
                m_playerName = reader.ReadString();
                break;
            case DataType.Bugle:
                m_bugleTitle = reader.ReadString();
                m_bugleContent = reader.ReadString();
                break;
            case DataType.Count:
                m_playerCount = reader.ReadInt32();
                break;
            case DataType.AddNoMsg:
            case DataType.RemoveNoMsg:
                m_playerGuid = reader.ReadGuid();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, NetworkBase networkBase, bool isServer)
    {
        if (isServer &&
            m_type != DataType.Create &&
            m_type != DataType.Modify &&
            m_type != DataType.SetUpdateLocation &&
            m_type != DataType.ClientKnownPlayer)
        {
            return;
        }
        PlayerData playerData = null;
        var subsystemPlayers = projectNet.FindSubsystem<SubsystemPlayers>();
        switch (m_type)
        {
            case DataType.Create:
                playerData = new PlayerData(projectNet);
                playerData.Load(m_vd);
                subsystemPlayers.AddPlayerData(playerData);
                //服务器广播给所有客户端，添加玩家
                foreach (var client in NetworkManager.Net.Clients)
                {
                    if (client.Value == NetworkManager.Net.Self) continue;
                    if (client.Value != playerData.Client)
                    {
                        NetworkManager.Net.QueuePackage(new PlayerDataPackage(playerData, true) { To = client.Value });
                    }
                    else
                    {
                        NetworkManager.Net.QueuePackage(new PlayerDataPackage(playerData, false) { To = client.Value });
                    }
                }
                break;
            case DataType.Modify:
                var playerData2 = subsystemPlayers.FindPlayerData(p => { return p.PlayerGUID == m_playerGuid; });
                if (playerData2 != null)
                {
                    Client client = From;

                    playerData2.CharacterSkinName = m_skinName;
                    playerData2.m_playerClass = m_playerClass;
                    if (isServer)
                    {
                        networkBase.QueuePackage(this);
                    }
                }
                break;
            case DataType.Delete:
                networkBase.RemoveClient(From);
                break;
            case DataType.AddPlayer:
                //客户端接收到添加玩家广播
                playerData = new PlayerData(projectNet);
                playerData.Load(m_vd);
                subsystemPlayers.AddPlayerData(playerData);
                networkBase.QueuePackage(new PlayerDataPackage(playerData, DataType.ClientKnownPlayer));
                break;
            case DataType.SetUpdateLocation:
                var player = subsystemPlayers.PlayersData.Find(x => x.Client == From);
                if (player != null)
                {
                    var updater = projectNet.FindSubsystem<SubsystemTerrain>().TerrainUpdater;
                    updater.SetLastChunksUpdateCenter(player.PlayerIndex, m_updateLocation.LastChunksUpdateCenter);
                    updater.SetUpdateLocation(player.PlayerIndex, m_updateLocation.Center, m_updateLocation.VisibilityDistance, m_updateLocation.ContentDistance);
                }
                break;
            case DataType.CloseTime:
                var p3 = projectNet.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (p3 != null)
                {
                    p3.ComponentGui.CloseTime = m_visibility;
                    DialogsManager.ShowDialog(null, new MessageDialog("服务器关闭提醒", m_playerName, LanguageControl.Yes, LanguageControl.No, btn =>
                    {
                        DialogsManager.HideAllDialogs();
                    }));
                }
                break;
            case DataType.Bugle:
                ComponentPlayer mainPlayer = projectNet.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (mainPlayer != null)
                {
                    if (m_playerGuid == Guid.Empty || (m_playerGuid != Guid.Empty && mainPlayer.PlayerData.PlayerGUID == m_playerGuid))
                    {
                        DialogsManager.HideAllDialogs();
                        mainPlayer.ComponentHealth.IsInvulnerable = true;
                        m_bugleContent = m_bugleContent.Replace("[n]", "\n").Replace("[e]", " ");
                        DialogsManager.ShowDialog(null, new MessageDialog(m_bugleTitle, m_bugleContent, LanguageControl.Ok, null, btn =>
                        {
                            DialogsManager.HideAllDialogs();
                            mainPlayer.ComponentHealth.IsInvulnerable = false;
                        }));
                    }
                }
                break;
            case DataType.Count:
                ComponentPlayer mainPlayer2 = projectNet.FindSubsystem<SubsystemPlayers>().MainPlayer;
                if (mainPlayer2 != null)
                {
                    int clientPlayerCount = projectNet.FindSubsystem<SubsystemPlayers>().PlayersData.Count;
                    Log.Information($"隐身测试，服务端人数：{m_playerCount}; 客户端人数：{clientPlayerCount}");
                    if (clientPlayerCount != m_playerCount)
                    {
                        ScreensManager.SwitchScreen("NetPlay");
                        GameManager.DisposeProject();
                        NetworkManager.Net.Stop();
                        DialogsManager.ShowDialog(null, new MessageDialog("连接异常", "检测到玩家人数异常，请重新连接服务器", LanguageControl.Ok, null, btn =>
                        {
                        }));
                    }
                }
                break;
            case DataType.AddNoMsg:
                projectNet.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerGuid.ToString());
                break;
            case DataType.RemoveNoMsg:
                projectNet.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerGuid.ToString());
                break;
        }
    }
}
