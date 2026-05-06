using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork.Packages;

public class PlayerCreatePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerCreate;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public ValuesDictionary m_vd;

    public PlayerCreatePackage() { }

    public PlayerCreatePackage(PlayerData playerData) {
        m_vd = new ValuesDictionary();
        playerData.Save(m_vd);
    }

    public override void Serialize(PackageSerializer archive) {
        byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_vd = new ValuesDictionary();
            m_vd.ApplyOverridesUseMessagePack(messagePack);
        }
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = new PlayerData(GameManager.Project);
        playerData.Load(m_vd);
        subsystemPlayers.AddPlayerData(playerData);
        // 服务端广播给所有客户端，添加玩家。
        foreach (var client in NetworkManager.Clients) {
            if (client.Value == NetworkManager.Self) continue;
            NetworkManager.QueuePackage(new PlayerAddPackage(playerData));
        }
    }
}

public class PlayerModifyPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerModify;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;

    public PlayerModifyPackage() { }

    public PlayerModifyPackage(PlayerData playerData) {
        m_playerGuid = playerData.PlayerGUID;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerClass = playerData.m_playerClass;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
        archive.Value(ref m_playerName);
        archive.Value(ref m_skinName);
        archive.Enum(ref m_playerClass);
    }

    public override void Handle(bool isServer) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = subsystemPlayers.FindPlayerData(p => p.PlayerGUID == m_playerGuid);
        if (playerData != null) {
            playerData.CharacterSkinName = m_skinName;
            playerData.m_playerClass = m_playerClass;
            if (isServer) {
                NetworkManager.QueuePackage(this);
            }
        }
    }
}

public class PlayerDeletePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerDelete;
    public override ClientStage MinNeedState => ClientStage.Connected;

    public override void Serialize(PackageSerializer archive) { }

    public override void Handle(bool isServer) {
        if (isServer) {
            NetworkManager.Server.RemoveClient(From);
        }
    }
}

public class PlayerAddPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerAdd;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public ValuesDictionary m_vd;

    public PlayerAddPackage() { }

    public PlayerAddPackage(PlayerData playerData) {
        m_vd = new ValuesDictionary();
        playerData.Save(m_vd);
    }

    public override void Serialize(PackageSerializer archive) {
        byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_vd = new ValuesDictionary();
            m_vd.ApplyOverridesUseMessagePack(messagePack);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = new PlayerData(GameManager.Project);
        playerData.Load(m_vd);
        subsystemPlayers.AddPlayerData(playerData);
        NetworkManager.QueuePackage(new PlayerKnownPackage(playerData.PlayerGUID));
    }
}

public class PlayerKnownPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerKnown;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerKnownPackage() { }

    public PlayerKnownPackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        // Typically server side would handle this to know client has loaded the player
    }
}

public class PlayerUpdateLocationPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerUpdateLocation;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public TerrainUpdater.UpdateLocation m_updateLocation;

    public PlayerUpdateLocationPackage() { }

    public PlayerUpdateLocationPackage(TerrainUpdater.UpdateLocation updateLocation) {
        m_updateLocation = updateLocation;
    }

    public override void Serialize(PackageSerializer archive) {
        if (archive.IsReading) {
            m_updateLocation = new TerrainUpdater.UpdateLocation();
        }
        Vector2 center = m_updateLocation.Center;
        ushort contentDistance = (ushort)m_updateLocation.ContentDistance;
        ushort visibilityDistance = (ushort)m_updateLocation.VisibilityDistance;
        Vector2? lastChunksUpdateCenter = m_updateLocation.LastChunksUpdateCenter;

        archive.Value(ref center);
        archive.Value(ref contentDistance);
        archive.Value(ref visibilityDistance);
        archive.Value(ref lastChunksUpdateCenter);

        m_updateLocation.Center = center;
        m_updateLocation.ContentDistance = contentDistance;
        m_updateLocation.VisibilityDistance = visibilityDistance;
        m_updateLocation.LastChunksUpdateCenter = lastChunksUpdateCenter;
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var player = subsystemPlayers.PlayersData.Find(x => x.Client == From);
        if (player != null) {
            var updater = GameManager.Project.FindSubsystem<SubsystemTerrain>().TerrainUpdater;
            updater.SetLastChunksUpdateCenter(player.PlayerIndex, m_updateLocation.LastChunksUpdateCenter);
            updater.SetUpdateLocation(player.PlayerIndex, m_updateLocation.Center, m_updateLocation.VisibilityDistance, m_updateLocation.ContentDistance);
        }
    }
}

public class ServerCloseTimePackage : IPackage {
    public override byte ID => (byte)PackageType.ServerCloseTime;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public int m_time;
    public string m_message;

    public ServerCloseTimePackage() { }

    public ServerCloseTimePackage(int time, string message) {
        m_time = time;
        m_message = message;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_time);
        archive.Value(ref m_message);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var p3 = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (p3 != null) {
            p3.ComponentGui.CloseTime = m_time;
            DialogsManager.ShowDialog(null, new MessageDialog("服务器关闭提醒", m_message, LanguageControl.Yes, LanguageControl.No, btn => {
                DialogsManager.HideAllDialogs();
            }));
        }
    }
}

public class PlayerBuglePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerBugle;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;
    public string m_title;
    public string m_content;

    public PlayerBuglePackage() { }

    public PlayerBuglePackage(Guid guid, string title, string content) {
        m_playerGuid = guid;
        m_title = title;
        m_content = content;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
        archive.Value(ref m_title);
        archive.Value(ref m_content);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (mainPlayer != null) {
            if (m_playerGuid == Guid.Empty || (m_playerGuid != Guid.Empty && mainPlayer.PlayerData.PlayerGUID == m_playerGuid)) {
                DialogsManager.HideAllDialogs();
                mainPlayer.ComponentHealth.IsInvulnerable = true;
                string content = m_content.Replace("[n]", "\n").Replace("[e]", " ");
                DialogsManager.ShowDialog(null, new MessageDialog(m_title, content, LanguageControl.Ok, null, btn => {
                    DialogsManager.HideAllDialogs();
                    mainPlayer.ComponentHealth.IsInvulnerable = false;
                }));
            }
        }
    }
}

public class PlayerCountPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerCount;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public int m_playerCount;

    public PlayerCountPackage() { }

    public PlayerCountPackage(int count) {
        m_playerCount = count;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerCount);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (mainPlayer != null) {
            int clientPlayerCount = GameManager.Project.FindSubsystem<SubsystemPlayers>().PlayersData.Count;
            Log.Information($"隐身测试，服务端人数：{m_playerCount}; 客户端人数：{clientPlayerCount}");
            if (clientPlayerCount != m_playerCount) {
                ScreensManager.SwitchScreen("NetPlay");
                GameManager.DisposeProject();
                NetworkManager.Stop();
                DialogsManager.ShowDialog(null, new MessageDialog("连接异常", "检测到玩家人数异常，请重新连接服务器", LanguageControl.Ok, null, btn => {
                }));
            }
        }
    }
}

public class PlayerNoMsgAddPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerNoMsgAdd;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgAddPackage() { }

    public PlayerNoMsgAddPackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerGuid.ToString());
    }
}

public class PlayerNoMsgRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerNoMsgRemove;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgRemovePackage() { }

    public PlayerNoMsgRemovePackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerGuid.ToString());
    }
}
