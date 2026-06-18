using System;
using HarmonyLib;

namespace Survivalcraft.TestMod.B {
    // 1. Intercept keyboard key down
    [HarmonyPatch(typeof(Engine.Input.Keyboard), "KeyDownHandler")]
    public static class Keyboard_KeyDownHandler_Patch {
        public static bool Prefix() {
            return false; // Skip original method, mute physical hardware
        }
    }

    // 2. Intercept keyboard key up
    [HarmonyPatch(typeof(Engine.Input.Keyboard), "KeyUpHandler")]
    public static class Keyboard_KeyUpHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    // 3. Intercept mouse move
    [HarmonyPatch(typeof(Engine.Input.Mouse), "MouseMoveHandler")]
    public static class Mouse_MouseMoveHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    // 4. Intercept mouse down
    [HarmonyPatch(typeof(Engine.Input.Mouse), "MouseDownHandler")]
    public static class Mouse_MouseDownHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    // 5. Intercept mouse up
    [HarmonyPatch(typeof(Engine.Input.Mouse), "MouseUpHandler")]
    public static class Mouse_MouseUpHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }
}
