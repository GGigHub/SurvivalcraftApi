using Engine;

namespace Game.NetWork;

public class NetRotation : NetSimulate<Quaternion> {
    public NetRotation(Quaternion quaternion) : base(quaternion) {
        LerpFunc = Lerp;
    }
    public static Quaternion Lerp(Quaternion rotation, Quaternion target, float f) {
        if (f >= 1f) return target;
        return Quaternion.Lerp(rotation, target, f);
    }
}
