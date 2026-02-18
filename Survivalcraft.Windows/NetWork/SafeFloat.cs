using System;

namespace Game.NetWork;

//说明 简单防止内存修改作弊
public class SafeFloat
{
    private int seed;
    private int value;
    private Random random;
    public SafeFloat()
    {
        random = new Random();
    }
    public float Get()
    {
        int i = value ^ seed;
        var data = BitConverter.GetBytes(i);
        return BitConverter.ToSingle(data, 0);
    }
    public void Set(float v)
    {
        seed = random.Int();
        var data = BitConverter.GetBytes(v);
        int i = BitConverter.ToInt32(data, 0);
        i ^= seed;
        value = i;
    }

}
