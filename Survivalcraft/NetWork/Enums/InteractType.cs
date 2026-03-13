using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game.NetWork;

[Flags]
public enum InteractType : byte
{
    None = 0,
    Interact = 1,
    Hit = 2,
    Aim = 4,
    Dig = 8,
    EndDig = 16,
    EndAim = 32,
    CancelAim = 64
}
