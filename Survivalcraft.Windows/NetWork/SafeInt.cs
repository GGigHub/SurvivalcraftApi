using System;
namespace Game.NetWork;

//说明 简单防止内存修改作弊
public class SafeInt
{
    private int seed;
    private int value;
    private Random random;
    public SafeInt()
    {
        random = new Random();
    }
    public int Get()
    {
        int i = value ^ seed;
        var data = BitConverter.GetBytes(i);
        return BitConverter.ToInt32(data, 0);
    }
    public void Set(int v)
    {
        seed = random.Int();
        var data = BitConverter.GetBytes(v);
        int i = BitConverter.ToInt32(data, 0);
        i ^= seed;
        value = i;
    }
}
