using System;
using System.Collections.Generic;
using System.Text;
using Engine;
using GameEntitySystem;
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
            // 前置拦截如校验黑名单
            From.NetworkGuid = playerGuid;
            SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
            if (subsystemPlayers.MakePlayerOnline(From.PlayerIndex, out PlayerData playerData, out Entity entity)) {
                if (entity != null) {
                    GameManager.Project.AddEntity(entity);
                }
                From.PlayerIndex = playerData.PlayerIndex;
                NetworkManager.Queue(new PlayerAddPacket(playerData) { Except = From });
            }
            byte[] data = GetNowProject(GameManager.Project);
            NetworkManager.Queue(new ProjectPacket(data, From.PlayerIndex) { To = From });
        }
    }

    public static byte[] GetNowProject(Project project) {
        ProjectData projectData = new ProjectData {
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
