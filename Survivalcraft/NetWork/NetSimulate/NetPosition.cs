using Engine;

namespace Game.NetWork;

public class NetPosition
{
    protected Vector3 Base;
    protected Vector3 Next;
    private float ElapsedTime;

    public NetPosition(Vector3 v)
    {
        Base = v;
        Next = v;
        ElapsedTime = 0;
    }

    public void SetNext(Vector3 v)
    {
        Base = Next;
        Next = v;
        ElapsedTime = 0;
    }

    public Vector3 Get(float dt)
    {
        ElapsedTime += dt * 8;
        if (ElapsedTime >= 1f) return Next;
        return Vector3.Lerp(Base, Next, ElapsedTime);
    }
}
