using System;
using System.Collections.Generic;
using System.Text;
using Engine;

namespace Game.NetWork {
    public class GenPackage : IPackage {
        // 这里应该由代码生成器判断哪个包id是空闲的
        public override byte ID => (byte)166;
        // 我们先不考虑ClientStage
        public override ClientStage MinNeedState => ClientStage.NotConnected;

        public byte ClientId;
        public Vector2 SendLookAngles;

        public GenPackage(byte id, Vector2 sendLookAngles) {
            ClientId = id;
            SendLookAngles = sendLookAngles;
        }

        // 因为代码中里用到了source，具体是source.ComponentLocomotion.SendLookAngles.Vaule
        // 所以序列化也只序列化这一项
        // 当然上面也有ClientId，是SyncByPlayer(byte? clientId, ....)传入的
        public override void Serialize(PackageSerializer archive) {
            archive.Value(ref ClientId);
            archive.Value(ref SendLookAngles);
        }

        // 这里是通过target.ComponentLocomotion.NetLookAngles.SetNext(....)分析出来的，
        // target就是GetComponentPlayer(ClientId)得出来的ComponentPlayer
        public override void Handle(bool isServer) {
            FindPlayer(ClientId).ComponentLocomotion.NetLookAngles.SetNext(SendLookAngles);
        }

        public ComponentPlayer FindPlayer(byte id) {
            // 按照clientId寻找玩家，待实现
            throw new NotImplementedException();
        }
    }
}
