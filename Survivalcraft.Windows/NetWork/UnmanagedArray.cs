using System;
using System.Runtime.InteropServices;

namespace Game.NetWork;

public class UnmanagedArray<T> where T : struct
{
    private IntPtr Ptr;
    private int elementSize;
    public int Length;
    public UnmanagedArray(int size)
    {
        Length = size;
        elementSize = Marshal.SizeOf(typeof(T));
        Ptr = Marshal.AllocHGlobal(size * elementSize);
    }
    public T this[int index]
    {
        get
        {
            return Marshal.PtrToStructure<T>(Ptr);
        }
        set
        {
            var addr = Ptr + (index * elementSize);
            Marshal.StructureToPtr(value, addr, true);
        }
    }

    public void Free()
    {
        Marshal.FreeHGlobal(Ptr);
    }
}
