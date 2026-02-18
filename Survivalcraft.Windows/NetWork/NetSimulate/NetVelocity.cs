using Engine;

namespace Game.NetWork;

public class NetVelocity : NetSimulate<Vector3>
{
    public NetVelocity(Vector3 vector) : base(vector)
    {
        LerpFunc = delegate (Vector3 b, Vector3 e, float f) {
            if (f >= 1f) return e;
            return Vector3.Lerp(b, e, f);
        };
    }
}
