using Engine;
using System;
using System.Collections.Generic;

namespace Game.NetWork.Packages;

public class PickablePackage : IPackage
{
    public byte ID => (byte)PackageType.Pickable;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public enum PickType
    {
        Create,
        Update,
        Delete,
        RequestSync,
        SetFlyToPosition,
        CreateList,
        DeleteList,
        SyncList,
    }

    public PickType m_type;

    public int m_id;
    public int m_value;
    public byte m_count;
    public Vector3 m_position;
    public Vector3 m_velocity;
    public Vector3? m_flyToPosition;
    public byte? m_getPlayer;
    public Vector3 m_positionFix;
    public float m_distance;
    public Matrix? stuckMatrix;
    public bool PlaySound;
    public List<Pickable> m_pickables = new List<Pickable>();
    public PickablePackage()
    {

    }
    public PickablePackage(List<Pickable> pickables, PickType type = PickType.Update)
    {
        m_type = type;
        m_pickables.AddRange(pickables);
    }
    public PickablePackage(Pickable pickable, PickType pickType)
    {
        m_id = pickable.Id;
        m_value = pickable.Value;
        m_count = (byte)pickable.Count;
        m_position = pickable.Position;
        m_velocity = pickable.Velocity;
        m_getPlayer = pickable.GetPickPlayer;
        stuckMatrix = pickable.StuckMatrix;
        m_flyToPosition = pickable.FlyToPosition;
        m_type = pickType;
        PlaySound = pickable.PlaySound;
    }

    public PickablePackage(int pickId)
    {
        m_id = pickId;
        m_type = PickType.Delete;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        SubsystemPickables subsystemPickables = GameManager.Project.FindSubsystem<SubsystemPickables>();
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case PickType.Create:
                writer.Write(m_id);
                writer.Write(m_count);
                writer.Write(m_value);
                writer.Write(m_position);
                writer.Write(m_velocity);
                writer.Write(stuckMatrix);
                break;
            case PickType.Update:
                writer.Write(m_pickables.Count);
                for (int i = 0; i < m_pickables.Count; i++)
                {
                    var t = m_pickables[i];
                    writer.Write(t.Id);
                    writer.Write(t.Position);
                }
                break;
            case PickType.SetFlyToPosition:
                writer.Write(m_id);
                writer.Write(m_flyToPosition.Value);
                break;
            case PickType.Delete:
                writer.Write(m_id);
                writer.Write(PlaySound);
                break;
            case PickType.CreateList:
                writer.Write(m_pickables.Count);
                foreach (Pickable pickable in m_pickables)
                {
                    writer.Write(pickable.Id);
                    writer.Write(pickable.Count);
                    writer.Write(pickable.Value);
                    writer.Write(pickable.Position);
                    writer.Write(pickable.Velocity);
                    writer.Write(pickable.StuckMatrix);
                }
                break;
            case PickType.DeleteList:
                writer.Write(m_pickables.Count);
                foreach (Pickable pickable in m_pickables)
                {
                    writer.Write(pickable.Id);
                }
                break;
            case PickType.RequestSync:
                writer.Write(m_id);
                break;
            case PickType.SyncList:
                {
                    var pickables = subsystemPickables.Pickables;
                    writer.Write(pickables.Count);
                    foreach (Pickable pickable in pickables)
                    {
                        writer.Write(pickable.Id);
                        writer.Write(pickable.Count);
                        writer.Write(pickable.Value);
                        writer.Write(pickable.Position);
                        writer.Write(pickable.Velocity);
                        writer.Write(pickable.StuckMatrix);
                    }
                }
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<PickType>();
        switch (m_type)
        {
            case PickType.Create:
                m_id = reader.ReadInt32();
                m_count = reader.ReadByte();
                m_value = reader.ReadInt32();
                m_position = reader.ReadVector3();
                m_velocity = reader.ReadVector3();
                stuckMatrix = reader.ReadMatrixNullable();
                break;
            case PickType.Update:
                {
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        Pickable pickable = new Pickable();
                        pickable.Id = reader.ReadUInt16();
                        pickable.Position = reader.ReadVector3();
                        m_pickables.Add(pickable);
                    }
                }
                break;
            case PickType.SetFlyToPosition:
                m_id = reader.ReadUInt16();
                m_flyToPosition = reader.ReadVector3();
                break;
            case PickType.Delete:
                m_id = reader.ReadUInt16();
                PlaySound = reader.ReadBoolean();
                break;
            case PickType.CreateList:
                {
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        Pickable pickable = new Pickable();
                        pickable.Id = reader.ReadUInt16();
                        pickable.Count = reader.ReadInt32();
                        pickable.Value = reader.ReadInt32();
                        pickable.Position = reader.ReadVector3();
                        pickable.Velocity = reader.ReadVector3();
                        pickable.StuckMatrix = reader.ReadMatrixNullable();
                        m_pickables.Add(pickable);
                    }
                }
                break;
            case PickType.DeleteList:
                {
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        Pickable pickable = new Pickable();
                        pickable.Id = reader.ReadUInt16();
                        m_pickables.Add(pickable);
                    }
                }
                break;
            case PickType.RequestSync:
                m_id = reader.ReadUInt16();
                break;
            case PickType.SyncList:
                {
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        Pickable pickable = new Pickable();
                        pickable.Id = reader.ReadUInt16();
                        pickable.Count = reader.ReadInt32();
                        pickable.Value = reader.ReadInt32();
                        pickable.Position = reader.ReadVector3();
                        pickable.Velocity = reader.ReadVector3();
                        pickable.StuckMatrix = reader.ReadMatrixNullable();
                        m_pickables.Add(pickable);
                    }
                }
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (isServer && m_type != PickType.RequestSync)
        {
            return;
        }
        var subsystemPickable = projectNet.FindSubsystem<SubsystemPickables>();
        bool flag = false;
        switch (m_type)
        {
            case PickType.Create:
                Pickable tmp = subsystemPickable.Pickables.Find(p => { return p.Id == m_id; });
                if (tmp != null)
                {
                    tmp.Value = m_value;
                    tmp.Count = m_count;
                    tmp.Velocity = m_velocity;
                    tmp.StuckMatrix = stuckMatrix;
                }
                else
                {
                    Pickable created = subsystemPickable.CreatePickable(m_id, m_value, (int)m_count, m_position, (Vector3?)m_velocity, stuckMatrix, null);
                    subsystemPickable.AddPickable(created);
                }
                break;
            case PickType.Update:
                foreach (var c in m_pickables)
                {
                    subsystemPickable.PickableAction(c.Id, (pick) => { pick.Position = c.Position; });
                }
                foreach (var c in subsystemPickable.Pickables)
                {
                    if (m_pickables.Find(x => x.Id == c.Id) == null)
                    {
                        subsystemPickable.m_pickablesToRemove.Add(c);
                    }
                }
                break;
            case PickType.Delete:
                flag = subsystemPickable.PickableAction(m_id, (pick) =>
                {
                    if (PlaySound) subsystemPickable.PlayPickableCollectedSound(pick);
                    subsystemPickable.m_pickables.Remove(pick);
                    subsystemPickable.useMaps.Remove(pick.Id);
                    subsystemPickable.PickableRemoved?.Invoke(pick);
                }, false);
                break;
            case PickType.RequestSync:
                flag = subsystemPickable.PickableAction(m_id, (pick) =>
                {
                    NetworkManager.QueuePackage(new PickablePackage(pick, PickType.Create) { To = From });
                });
                if (!flag)
                {
                    NetworkManager.QueuePackage(new PickablePackage(m_id) { To = From });
                }
                break;
            case PickType.SetFlyToPosition:
                subsystemPickable.PickableAction(m_id, (pick) =>
                {
                    pick.FlyToPosition = m_flyToPosition;
                });
                break;
            case PickType.SyncList:
            case PickType.CreateList:
                if (isServer == false)
                {
                    foreach (Pickable pickable in m_pickables)
                    {
                        Pickable created = subsystemPickable.CreatePickable(pickable.Id, pickable.Value, (int)pickable.Count, pickable.Position, (Vector3?)pickable.Velocity, pickable.StuckMatrix, null);
                        subsystemPickable.AddPickable(created);
                    }
                }
                break;
            case PickType.DeleteList:
                if (isServer == false)
                {
                    foreach (Pickable pickable in m_pickables)
                    {
                        flag = subsystemPickable.PickableAction(pickable.Id, (pick) =>
                        {
                            subsystemPickable.m_pickables.Remove(pick);
                            subsystemPickable.useMaps.Remove(pick.Id);
                            subsystemPickable.PickableRemoved?.Invoke(pick);
                        }, false);
                    }
                }
                break;
        }
    }
}
