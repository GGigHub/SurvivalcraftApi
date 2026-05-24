using Engine;
using GameEntitySystem;

namespace Game.Network;

public static class NetworkLogicBlock {
    public static void OnEditAdjustableDelayGateItem(SubsystemAdjustableDelayGateBlockBehavior subsystem, int inventoryId, int slotIndex, int newDelay) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new AdjustableDelayGatePacket(default, true, inventoryId, slotIndex, newDelay));
        }
    }

    public static void OnEditAdjustableDelayGateBlock(SubsystemAdjustableDelayGateBlockBehavior subsystem, CellFace cell, int newDelay) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new AdjustableDelayGatePacket(cell, false, default, default, newDelay));
        }
    }

    public static void OnEditBatteryItem(SubsystemBatteryBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new BatteryVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        }
    }

    public static void OnEditBatteryBlock(SubsystemBatteryBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel));
        }
    }

    public static void OnEditButtonItem(SubsystemButtonBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new ButtonVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        }
    }

    public static void OnEditButtonBlock(SubsystemButtonBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new ButtonVoltagePacket(cell, false, default, default, newVoltageLevel));
        }
    }

    public static void OnEditMemoryBankItem(SubsystemMemoryBankBlockBehavior subsystem, int inventoryId, int slotIndex, MemoryBankData data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new MemoryBankDataPacket(default, true, inventoryId, slotIndex, data));
        }
    }

    public static void OnEditMemoryBankBlock(SubsystemMemoryBankBlockBehavior subsystem, CellFace cell, MemoryBankData data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new MemoryBankDataPacket(cell, false, default, default, data));
        }
    }

    public static void OnEditPistonItem(SubsystemPistonBlockBehavior subsystem, int inventoryId, int slotIndex, int data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new PistonDataPacket(default, true, inventoryId, slotIndex, data));
        }
    }

    public static void OnEditPistonBlock(SubsystemPistonBlockBehavior subsystem, CellFace cell, int data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new PistonDataPacket(cell, false, default, default, data));
        }
    }

    public static void OnEditSwitchItem(SubsystemSwitchBlockBehavior subsystem, int inventoryId, int slotIndex, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new SwitchVoltagePacket(default, true, inventoryId, slotIndex, newVoltageLevel));
        }
    }

    public static void OnEditSwitchBlock(SubsystemSwitchBlockBehavior subsystem, CellFace cell, int newVoltageLevel) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new SwitchVoltagePacket(cell, false, default, default, newVoltageLevel));
        }
    }

    public static void OnEditTruthTableCircuitItem(SubsystemTruthTableCircuitBlockBehavior subsystem, int inventoryId, int slotIndex, TruthTableData data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new TruthTableDataPacket(default, true, inventoryId, slotIndex, data));
        }
    }

    public static void OnEditTruthTableCircuitBlock(SubsystemTruthTableCircuitBlockBehavior subsystem, CellFace cell, TruthTableData data) {
        if (NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new TruthTableDataPacket(cell, false, default, default, data));
        }
    }

    public static void OnMemoryBankItemDataSync(SubsystemMemoryBankBlockBehavior subsystem, int id, MemoryBankData data) {
        if (!NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new MemoryBankDataPacket(id, data));
        }
    }

    public static void OnTruthTableItemDataSync(SubsystemTruthTableCircuitBlockBehavior subsystem, int id, TruthTableData data) {
        if (!NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new TruthTableDataPacket(id, data));
        }
    }
}
