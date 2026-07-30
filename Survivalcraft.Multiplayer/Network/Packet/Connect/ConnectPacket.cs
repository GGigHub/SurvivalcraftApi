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
            if (From == null
                || From.CompatibilityState != NetworkCompatibilityState.Accepted) {
                NetworkCompatibilityRuntime.RejectPrematurePacket(From, this);
                return;
            }
            if (From.AdmissionState == NetworkSessionAdmissionState.Accepted) {
                if (From.NetworkGuid != playerGuid) {
                    NetworkSessionAdmissionRuntime.RejectServerSession(
                        From,
                        NetworkSessionAdmission.CreateIdentityChangedResult());
                    return;
                }
                SendAcceptedSessionState(From);
                return;
            }
            if (From.AdmissionState != NetworkSessionAdmissionState.AwaitingRequest) {
                NetworkSessionAdmissionRuntime.RejectPrematurePacket(From, this);
                return;
            }
            if (playerGuid == Guid.Empty) {
                NetworkSessionAdmissionRuntime.RejectServerSession(
                    From,
                    NetworkSessionAdmission.CreateInvalidIdentityResult());
                return;
            }

            ClientSession existingSession = NetworkManager.ServerSessions.FirstOrDefault(
                session => session != null
                    && session != From
                    && session.NetworkGuid == playerGuid
                    && session.AdmissionState == NetworkSessionAdmissionState.Accepted
                    && session.NetworkState != NetworkState.NotConnected);
            if (existingSession != null) {
                existingSession.AdmissionResult =
                    NetworkSessionAdmission.CreateSupersededResult();
                existingSession.AdmissionState = NetworkSessionAdmissionState.Rejected;
                NetworkSessionLifecycleTracker.RecordSuperseded(existingSession);
                NetworkManager.DisconnectSession(
                    existingSession,
                    existingSession.AdmissionResult.Summary);
            }

            From.NetworkGuid = playerGuid;
            From.AdmissionResult = NetworkSessionAdmissionResult.Accept();
            From.AdmissionState = NetworkSessionAdmissionState.Accepted;
            From.AdmissionStartedUtc = default;
            NetworkSessionLifecycleTracker.RecordAdmissionAccepted(From);
            SendAcceptedSessionState(From);
        }
    }

    private static void SendAcceptedSessionState(ClientSession session) {
        NetworkManager.Queue(new SessionAdmissionResultPacket(
            NetworkSessionAdmissionResult.Accept()) { To = session });
        SubsystemNetwork subsystemNetwork =
            GameManager.Project.FindSubsystem<SubsystemNetwork>();
        if (session.PlayerIndex < 0
            && subsystemNetwork.MakePlayerOnline(session.NetworkGuid, out PlayerData playerData)) {
            session.PlayerIndex = playerData.PlayerIndex;
            NetworkSessionLifecycleTracker.RecordPlayerAssigned(session);
            NetworkManager.Queue(new PlayerAddPacket(playerData) { Except = session });
            NetworkManager.Queue(new IndexPacket(session.PlayerIndex) { To = session });
        }
        else if (session.PlayerIndex >= 0) {
            NetworkManager.Queue(new IndexPacket(session.PlayerIndex) { To = session });
        }
        byte[] data = GetNowProject(GameManager.Project);
        NetworkManager.Queue(new ProjectPacket(data) { To = session });
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
