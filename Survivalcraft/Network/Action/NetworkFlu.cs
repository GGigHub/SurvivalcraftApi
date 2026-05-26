using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.Network {
    public static class NetworkFlu {
        public static void OnFluUpdate(ComponentFlu flu) {
            if (!NetworkService.IsClient && Time.PeriodicEvent(1.0, 0.5)) {
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.SyncStat));
            }
        }

        public static void OnFluEffectEvent(ComponentFlu flu) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.FluEffect));
            }
        }

        public static void OnStartFluEvent(ComponentFlu flu) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.StartFlu));
            }
        }

        public static void OnSneezeEvent(ComponentFlu flu) {
            if (!NetworkService.IsClient) {
                NetworkService.QueuePacket(new ComponentFluPacket(flu, ComponentFluPacket.EventType.Sneeze));
            }
        }
    }
}
