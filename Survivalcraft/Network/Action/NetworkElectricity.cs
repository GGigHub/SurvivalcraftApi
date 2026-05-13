using System.Collections.Generic;

namespace Game.Network;

public static class NetworkElectricity {
    public static void OnElectricityPersistentVoltageChanged(SubsystemElectricity subsystem, List<SubsystemElectricity.NetSimulate> list) {
        if (!NetworkManager.IsClient) {
            NetworkManager.QueuePacket(new SubsystemElectricityPacket(list));
        }
    }
}
