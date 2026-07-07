using System;
using System.Reflection;
using Engine;
using Engine.Input;
using Game;
using HarmonyLib;

namespace Survivalcraft.TestAutomation {
    public static class InputHijackState {
        private static readonly FieldInfo MouseMovementField =
            AccessTools.Field(typeof(Mouse), "<MouseMovement>k__BackingField");

        public static bool IsInjectingMouseMove { get; set; }

        public static Point2? QueuedMouseMovement { get; set; }

        public static Vector3 HeldMoveInput { get; private set; }

        public static int HeldMoveInputEndFrame { get; private set; } = -1;

        public static void SetMouseMovement(Point2 movement) {
            MouseMovementField.SetValue(null, movement);
        }

        public static void HoldMoveInput(Vector3 move, int frameCount) {
            HeldMoveInput = move;
            HeldMoveInputEndFrame = Time.FrameIndex + Math.Max(1, frameCount);
        }

        public static bool TryGetHeldMoveInput(out Vector3 move) {
            if (Time.FrameIndex < HeldMoveInputEndFrame) {
                move = HeldMoveInput;
                return true;
            }

            move = Vector3.Zero;
            return false;
        }
    }

    // 1. Intercept keyboard key down
    [HarmonyPatch(typeof(Keyboard), "KeyDownHandler")]
    public static class Keyboard_KeyDownHandler_Patch {
        public static bool Prefix() {
            return false; // Skip original method, mute physical hardware
        }
    }

    // 2. Intercept keyboard key up
    [HarmonyPatch(typeof(Keyboard), "KeyUpHandler")]
    public static class Keyboard_KeyUpHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    // 3. Intercept mouse move
    [HarmonyPatch(typeof(Mouse), "ProcessMouseMove")]
    public static class Mouse_ProcessMouseMove_Patch {
        public static bool Prefix() {
            return InputHijackState.IsInjectingMouseMove;
        }
    }

    // 4. Intercept relative mouse movement computed during the frame
    [HarmonyPatch(typeof(Mouse), "BeforeFrame")]
    public static class Mouse_BeforeFrame_Patch {
        public static void Postfix() {
            Point2 mouseMovement = InputHijackState.QueuedMouseMovement ?? Point2.Zero;
            InputHijackState.QueuedMouseMovement = null;
            InputHijackState.SetMouseMovement(mouseMovement);
        }
    }

    // 5. Intercept mouse down
    [HarmonyPatch(typeof(Mouse), "MouseDownHandler")]
    public static class Mouse_MouseDownHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    // 6. Intercept mouse up
    [HarmonyPatch(typeof(Mouse), "MouseUpHandler")]
    public static class Mouse_MouseUpHandler_Patch {
        public static bool Prefix() {
            return false;
        }
    }

    [HarmonyPatch(typeof(ComponentInput), "Update")]
    public static class ComponentInput_Update_Patch {
        public static void Postfix(ComponentInput __instance) {
            if (__instance.m_componentPlayer?.PlayerData?.IsMainPlayer != true
                || !InputHijackState.TryGetHeldMoveInput(out Vector3 move)) {
                return;
            }

            __instance.m_playerInput.Move = move;
            __instance.m_playerInput.CrouchMove = move;
            __instance.m_playerInput.CameraMove = move;
            __instance.m_playerInput.CameraCrouchMove = move;
        }
    }
}
