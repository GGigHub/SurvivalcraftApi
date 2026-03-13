using Engine;
using System.Collections.Generic;
using System.Linq;

namespace Game.NetWork.Packages;

public class FurniturePackage : IPackage
{
    public byte ID => (byte)PackageType.Furniture;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public enum EventType
    {
        RequestAdd,
        Add,
        NewFurnitureSet,
        DeleteFurnitureSet,
        RenameFurnitureSet,
        MoveFurnitureSet,
        AddToFurnitureSet,
        TryAddDesignChain,
        RemoveFurnitureDesigns
    }
    public EventType eventType;
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public string addXml;
    public string fromName;
    public int furnitureIndex;
    public CellFace cellFace;
    public int startValue;
    public Dictionary<Point3, int> list = new Dictionary<Point3, int>();
    public List<int> toRemoveList = new List<int>();

    public FurniturePackage()
    {
    }

    public FurniturePackage(FurnitureSet furnitureSet)
    {
        eventType = EventType.NewFurnitureSet;
        addXml = furnitureSet.Name;
        fromName = furnitureSet.ImportedFrom;
    }

    public FurniturePackage(List<int> list)
    {
        eventType = EventType.RemoveFurnitureDesigns;
        toRemoveList.AddRange(list);
    }


    public FurniturePackage(string designName)
    {
        eventType = EventType.DeleteFurnitureSet;
        addXml = designName;
    }

    public FurniturePackage(string oldName, string newName)
    {
        eventType = EventType.RenameFurnitureSet;
        addXml = oldName;
        fromName = newName;
    }

    public FurniturePackage(FurnitureSet furnitureSet, int move)
    {
        eventType = EventType.MoveFurnitureSet;
        furnitureIndex = move;
        addXml = furnitureSet.Name;
    }

    public FurniturePackage(FurnitureDesign design, FurnitureSet furnitureSet)
    {
        eventType = EventType.AddToFurnitureSet;
        furnitureIndex = design.Index;
        addXml = furnitureSet.Name;
    }
    public FurniturePackage(FurnitureDesign design, bool garbageCollectIfNeeded)
    {
        eventType = EventType.TryAddDesignChain;
        furnitureIndex = design.Index;
        var dict = design.Save();
        addXml = CommonLib.SerializeVDict(dict);
        startValue = garbageCollectIfNeeded ? 1 : 0;
    }

    public FurniturePackage(FurnitureDesign design, Dictionary<Point3, int> m_list, CellFace cellFace_, int value, bool isRequest = false)
    {
        eventType = isRequest ? EventType.RequestAdd : EventType.Add;
        furnitureIndex = design.Index;
        var dict = design.Save();
        addXml = CommonLib.SerializeVDict(dict);
        foreach (var k in m_list)
        {
            list.Add(k.Key, k.Value);
        }
        cellFace = cellFace_;
        startValue = value;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(eventType);
        switch (eventType)
        {
            case EventType.TryAddDesignChain:
                writer.Write(addXml);
                writer.Write(furnitureIndex);
                writer.Write(startValue);
                break;
            case EventType.AddToFurnitureSet:
            case EventType.MoveFurnitureSet:
                writer.Write(addXml);
                writer.Write(furnitureIndex);
                break;
            case EventType.RenameFurnitureSet:
                writer.Write(addXml);
                writer.Write(fromName);
                break;
            case EventType.DeleteFurnitureSet:
                writer.Write(addXml);
                break;
            case EventType.NewFurnitureSet:
                writer.Write(addXml);
                writer.Write(fromName != null);
                if (fromName != null) writer.Write(fromName);
                break;
            case EventType.RemoveFurnitureDesigns:
                writer.Write(toRemoveList.Count);
                foreach (var item in toRemoveList)
                {
                    writer.Write(item);
                }
                break;
            case EventType.Add:
            case EventType.RequestAdd:
                writer.Write(furnitureIndex);
                writer.Write(addXml);
                writer.Write(list.Count);
                foreach (var k in list)
                {
                    writer.Write(k.Key);
                    writer.Write(k.Value);
                }
                writer.Write(cellFace);
                writer.Write(startValue);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        eventType = reader.ReadEnum<EventType>();
        switch (eventType)
        {
            case EventType.TryAddDesignChain:
                addXml = reader.ReadString();
                startValue = reader.ReadInt32();
                furnitureIndex = reader.ReadInt32();
                break;
            case EventType.AddToFurnitureSet:
            case EventType.MoveFurnitureSet:
                addXml = reader.ReadString();
                furnitureIndex = reader.ReadInt32();
                break;
            case EventType.RenameFurnitureSet:
                addXml = reader.ReadString();
                fromName = reader.ReadString();
                break;
            case EventType.DeleteFurnitureSet:
                addXml = reader.ReadString();
                break;
            case EventType.NewFurnitureSet:
                addXml = reader.ReadString();
                if (reader.ReadBoolean()) fromName = reader.ReadString();
                break;
            case EventType.RemoveFurnitureDesigns:
                int c = reader.ReadInt32();
                for (int i = 0; i < c; i++)
                {
                    toRemoveList.Add(reader.ReadInt32());
                }
                break;
            case EventType.Add:
            case EventType.RequestAdd:
                furnitureIndex = reader.ReadInt32();
                addXml = reader.ReadString();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    list.Add(reader.ReadPoint3(), reader.ReadInt32());
                }
                cellFace = reader.ReadCellFace();
                startValue = reader.ReadInt32();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        FurnitureSet furnitureSet = null;
        FurnitureDesign furniture = null;
        TemplatesDatabase.ValuesDictionary vdict = null;
        SubsystemPlayers subsystemPlayers = projectNet.FindSubsystem<SubsystemPlayers>();
        FurnitureInventoryPanel furnitureInventoryPanel = projectNet.FindFurnitureInventorPanel(From);
        if (furnitureInventoryPanel == null) return;
        SubsystemTerrain subsystemTerrain = projectNet.FindSubsystem<SubsystemTerrain>();
        SubsystemFurnitureBlockBehavior subsystemFurnitureBlockBehavior = projectNet.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        switch (eventType)
        {
            case EventType.TryAddDesignChain:
                vdict = CommonLib.ReadVDict(addXml);
                furniture = new FurnitureDesign(furnitureIndex, subsystemTerrain, vdict);
                subsystemFurnitureBlockBehavior.TryAddDesignChain(furniture, startValue == 1);
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.AddToFurnitureSet:
                furnitureSet = subsystemFurnitureBlockBehavior.m_furnitureSets.Find(f => f.Name == addXml);
                furniture = subsystemFurnitureBlockBehavior.m_furnitureDesigns.FirstOrDefault(f => f.Index == furnitureIndex);
                if (furniture != null)
                {
                    subsystemFurnitureBlockBehavior.AddToFurnitureSet(furniture, furnitureSet);
                    furnitureInventoryPanel?.Invalidate();
                }
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.MoveFurnitureSet:
                furnitureSet = subsystemFurnitureBlockBehavior.m_furnitureSets.Find(f => f.Name == addXml);
                if (furnitureSet != null)
                {
                    subsystemFurnitureBlockBehavior.MoveFurnitureSet(furnitureSet, furnitureIndex);
                    furnitureInventoryPanel?.Invalidate();
                }
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.RenameFurnitureSet:
                furnitureSet = subsystemFurnitureBlockBehavior.m_furnitureSets.Find(f => f.Name == addXml);
                if (furnitureSet != null)
                {
                    furnitureSet.Name = addXml;
                    furnitureInventoryPanel?.Invalidate();
                }
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.DeleteFurnitureSet:
                furnitureSet = subsystemFurnitureBlockBehavior.m_furnitureSets.Find(f => f.Name == addXml);
                if (furnitureSet != null)
                {
                    int num = subsystemFurnitureBlockBehavior.FurnitureSets.IndexOf(furnitureSet);
                    subsystemFurnitureBlockBehavior.DeleteFurnitureSet(furnitureSet);
                    subsystemFurnitureBlockBehavior.GarbageCollectDesigns();
                    if (furnitureInventoryPanel != null)
                    {
                        if (furnitureInventoryPanel.ComponentFurnitureInventory.FurnitureSet?.Name == addXml)
                            furnitureInventoryPanel.ComponentFurnitureInventory.FurnitureSet = (num > 0) ? subsystemFurnitureBlockBehavior.FurnitureSets[num - 1] : null;
                        furnitureInventoryPanel.Invalidate();
                    }
                }
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.NewFurnitureSet:
                furnitureInventoryPanel?.NewFurnitueSetLogic(addXml, fromName);
                if (isServer)
                {
                    Except = From;
                    netNode.QueuePackage(this);
                }
                break;
            case EventType.Add:
                vdict = CommonLib.ReadVDict(addXml);
                furniture = new FurnitureDesign(furnitureIndex, subsystemTerrain, vdict);
                subsystemFurnitureBlockBehavior.CreateDesign(subsystemPlayers.MainPlayer?.ComponentMiner, furniture, list, cellFace, startValue, false);
                break;
            case EventType.RequestAdd:
                vdict = CommonLib.ReadVDict(addXml);
                furniture = new FurnitureDesign(0, subsystemTerrain, vdict);
                subsystemPlayers.FindPlayerByClientId(From.ID, (player) =>
                {
                    // 校验：必须手持家具锤
                    int activeValue = player.ComponentMiner.ActiveBlockValue;
                    int activeContents = Terrain.ExtractContents(activeValue);
                    Block activeBlock = BlocksManager.Blocks[activeContents];
                    if (!(activeBlock is HammerBlock))
                    {
                        // 不执行操作
                        CommonLib.Net.RemoveClient(From, "异常操作");
                        return;
                    }

                    furniture = subsystemFurnitureBlockBehavior.CreateDesign(player.ComponentMiner, furniture, list, cellFace, startValue);
                    // 回复添加家具包
                    netNode.QueuePackage(new FurniturePackage(furniture, list, cellFace, startValue));
                });
                break;
            case EventType.RemoveFurnitureDesigns:
                for (int k = 0; k < subsystemFurnitureBlockBehavior.m_furnitureDesigns.Length; k++)
                {
                    var obj = subsystemFurnitureBlockBehavior.m_furnitureDesigns[k];
                    if (obj == null) continue;
                    foreach (var item in toRemoveList)
                    {
                        if (obj.Index == item)
                        {
                            obj.Index = -1;
                            subsystemFurnitureBlockBehavior.m_furnitureDesigns[k] = null;
                            break;
                        }
                    }
                }
                break;
        }
    }
}
