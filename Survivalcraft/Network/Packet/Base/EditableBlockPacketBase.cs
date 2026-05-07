using System;
using System.Collections.Generic;
using System.Text;
using GameEntitySystem;
using Engine;

namespace Game.Network;

public abstract class EditableBlockPacketBase : Packet {
    public static void SerializeEditableTarget(PacketSerializer archive, ref bool editAsItem, ref int inventoryId, ref int slotIndex, ref CellFace cellFace) {
        archive.Value(ref editAsItem);
        if (editAsItem) {
            archive.Value(ref inventoryId);
            archive.Value(ref slotIndex);
        }
        else {
            archive.Value(ref cellFace);
        }
    }

    public static void SerializeOptionalId(PacketSerializer archive, ref int? id) {
        bool hasId = id.HasValue;
        archive.Value(ref hasId);
        if (hasId) {
            int value = id ?? 0;
            archive.Value(ref value);
            if (archive.IsReading) {
                id = value;
            }
        }
        else if (archive.IsReading) {
            id = null;
        }
    }

    public static void SerializeByteArray(PacketSerializer archive, ref byte[] data) {
        ushort count = archive.IsWriting ? (ushort)data.Length : (ushort)0;
        archive.Value(ref count);
        byte[] buffer = archive.IsWriting ? data : null;
        archive.Raw(ref buffer, count);
        if (archive.IsReading) {
            data = buffer;
        }
    }

    public static int ReplaceDataAtSlot(IInventory inventory, int slotIndex, Func<int, int> newData) {
        int value = inventory.GetSlotValue(slotIndex);
        int count = inventory.GetSlotCount(slotIndex);
        inventory.RemoveSlotItems(slotIndex, count);
        int newValue = Terrain.ReplaceData(value, newData(Terrain.ExtractData(value)));
        inventory.AddSlotItems(slotIndex, newValue, count);
        return newValue;
    }
}
