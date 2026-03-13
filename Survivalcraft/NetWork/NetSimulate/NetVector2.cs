using Engine;

namespace Game.NetWork;

public class NetVector2 : NetSimulate<Vector2>
{
    public NetVector2(Vector2 vector) : base(vector)
    {
        LerpFunc = delegate (Vector2 b, Vector2 e, float f) {
            if (f >= 1f) return e;
            return Vector2.Lerp(b, e, f);
        };
    }
}
