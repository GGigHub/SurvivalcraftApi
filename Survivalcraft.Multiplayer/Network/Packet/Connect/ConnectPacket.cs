using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engine;
using GameEntitySystem;
using Silk.NET.OpenXR;
using TemplatesDatabase;

namespace Game.Network;

public class ConnectPacket : Packet {
    public override byte ID => (byte)PacketType.ConnectInfo;

    public Guid playerGuid;
    public ConnectPacket() {
    }

    public ConnectPacket(Guid guid) {
        playerGuid = guid;
    }

    public override void Serialize(PacketSerializer serializer) {
        serializer.Value(ref playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            if (From == null) {
                return;
            }

            var existing = NetworkManager.ServerSessions
                .FirstOrDefault(s => s != null && s != From && s.NetworkGuid == playerGuid);
            if (playerGuid != Guid.Empty && existing != null) {
                const string reason = "该 GUID 已经在游戏中，禁止重复进入。";
                (NetworkManager.NetworkServer as NetworkServer)?.Disconnect(From);
                existing.NetworkState = NetworkState.NotConnected; // 断开已有会话
                From.NetworkState = NetworkState.NotConnected;     // 同时拒绝当前连接
                Log.Warning($"Rejected duplicate network GUID: {playerGuid}");
                return;
            }

            // 前置拦截如校验黑名单
            From.NetworkGuid = playerGuid;
            SubsystemNetwork subsystemNetwork = GameManager.Project.FindSubsystem<SubsystemNetwork>();
            if (subsystemNetwork.MakePlayerOnline(From.NetworkGuid, out PlayerData playerData)) {
                From.PlayerIndex = playerData.PlayerIndex;
                NetworkManager.Queue(new PlayerAddPacket(playerData) { Except = From });
                NetworkManager.Queue(new IndexPacket(From.PlayerIndex) { To = From });
            }
            byte[] data = GetNowProject(GameManager.Project);
            NetworkManager.Queue(new ProjectPacket(data) { To = From });
        }
    }

    public static byte[] GetNowProject(Project project) {
        ProjectData projectData = new ProjectData {
            NextEntityID = project.NextEntityID,
            ValuesDictionary = new ValuesDictionary {
                DatabaseObject = project.m_projectTemplate
            }
        };
        foreach (Subsystem subsystem in project.m_subsystems) {
            ValuesDictionary valuesDictionary = new ValuesDictionary();
            subsystem.Save(valuesDictionary);
            if (valuesDictionary.Count > 0) {
                projectData.ValuesDictionary.SetValue(subsystem.ValuesDictionary.DatabaseObject.Name, valuesDictionary);
            }
        }
        ValuesDictionary rootNode = new ValuesDictionary();
        projectData.Save(rootNode);
        return rootNode.ToMessagePack();
    }
}

