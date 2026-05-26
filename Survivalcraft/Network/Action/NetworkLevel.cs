using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Network {
    public static class NetworkLevel {
        public static void OnAddExperience(ComponentLevel level, int count, bool playSound) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new PlayerAddExperiencePacket(level.m_componentPlayer.PlayerData, count, playSound, level.m_componentPlayer.PlayerData.Level));
            }
        }
    }
}
