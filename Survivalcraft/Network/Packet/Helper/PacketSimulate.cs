using Engine;

namespace Game;

public abstract class NetworkSimulate<T> {
    protected T Base;
    protected T Next;
    private float ElapsedTime;
    protected Func<T, T, float, T> LerpFunc;

    public NetworkSimulate(T v) {
        Base = v;
        Next = v;
        ElapsedTime = 0;
    }

    public virtual void SetNext(T v) {
        Base = Next;
        Next = v;
        ElapsedTime = 0;
    }

    public virtual T Get(float dt) {
        ElapsedTime += dt * 8;
        return LerpFunc(Base, Next, ElapsedTime);
    }
}

public class NetVector2 : NetworkSimulate<Vector2> {
    public NetVector2(Vector2 vector) : base(vector) {
        LerpFunc = delegate (Vector2 b, Vector2 e, float f) {
            if (f >= 1f) return e;
            return Vector2.Lerp(b, e, f);
        };
    }
}

public class NetRotation : NetworkSimulate<Quaternion> {
    public NetRotation(Quaternion quaternion) : base(quaternion) {
        LerpFunc = Lerp;
    }
    public static Quaternion Lerp(Quaternion rotation, Quaternion target, float f) {
        if (f >= 1f) return target;
        return Quaternion.Lerp(rotation, target, f);
    }
}

public class NetPosition : NetworkSimulate<Vector3> {
    private float ElapsedTime;

    public NetPosition(Vector3 v) : base(v) {
        Base = v;
        Next = v;
        ElapsedTime = 0;
    }

    public override void SetNext(Vector3 v) {
        Base = Next;
        Next = v;
        ElapsedTime = 0;
    }

    public override Vector3 Get(float dt) {
        ElapsedTime += dt * 8;
        if (ElapsedTime >= 1f) return Next;
        return Vector3.Lerp(Base, Next, ElapsedTime);
    }
}

