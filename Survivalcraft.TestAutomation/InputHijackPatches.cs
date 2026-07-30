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

        public static Vector2 HeldLookInput { get; private set; }

        public static int HeldLookInputEndFrame { get; private set; } = -1;

        public static bool QueuedToggleCrouch { get; private set; }

        public static Ray3? QueuedInteractRay { get; private set; }

        public static Ray3? QueuedHitRay { get; private set; }

        public static Ray3? HeldDigRay { get; private set; }

        public static int HeldDigRayEndFrame { get; private set; } = -1;

        public static Ray3? HeldAimRay { get; private set; }

        public static int HeldAimRayEndFrame { get; private set; } = -1;

        public static int AimInjectionCount { get; private set; }

        public static int LastAimInjectionFrame { get; private set; } = -1;

        public static bool QueuedDrop { get; private set; }

        public static void SetMouseMovement(Point2 movement) {
            MouseMovementField.SetValue(null, movement);
        }

        public static void HoldMoveInput(Vector3 move, int frameCount) {
            HeldMoveInput = move;
            HeldMoveInputEndFrame = Time.FrameIndex + Math.Max(1, frameCount);
        }

        public static void QueueInteractRay(Ray3 ray) {
            QueuedInteractRay = ray;
        }

        public static void QueueHitRay(Ray3 ray) {
            QueuedHitRay = ray;
        }

        public static void HoldDigRay(Ray3 ray, int frameCount) {
            HeldDigRay = ray;
            HeldDigRayEndFrame = Time.FrameIndex + Math.Max(1, frameCount);
        }

        public static void HoldLookInput(Vector2 look, int frameCount) {
            HeldLookInput = look;
            HeldLookInputEndFrame = Time.FrameIndex + Math.Max(1, frameCount);
        }

        public static void QueueToggleCrouch() {
            QueuedToggleCrouch = true;
        }

        public static void CancelDigRay() {
            HeldDigRay = null;
            HeldDigRayEndFrame = -1;
        }

        public static void HoldAimRay(Ray3 ray, int frameCount) {
            HeldAimRay = ray;
            HeldAimRayEndFrame = Time.FrameIndex + Math.Max(1, frameCount);
            AimInjectionCount = 0;
            LastAimInjectionFrame = -1;
        }

        public static void RecordAimInjection() {
            AimInjectionCount++;
            LastAimInjectionFrame = Time.FrameIndex;
        }

        public static void QueueDrop() {
            QueuedDrop = true;
        }

        public static void ResetGameplayInputs() {
            HeldMoveInput = Vector3.Zero;
            HeldMoveInputEndFrame = -1;
            HeldLookInput = Vector2.Zero;
            HeldLookInputEndFrame = -1;
            QueuedToggleCrouch = false;
            QueuedInteractRay = null;
            QueuedHitRay = null;
            CancelDigRay();
            HeldAimRay = null;
            HeldAimRayEndFrame = -1;
            AimInjectionCount = 0;
            LastAimInjectionFrame = -1;
            QueuedDrop = false;
        }

        public static bool TryGetHeldMoveInput(out Vector3 move) {
            if (Time.FrameIndex < HeldMoveInputEndFrame) {
                move = HeldMoveInput;
                return true;
            }

            move = Vector3.Zero;
            return false;
        }

        public static bool TryGetHeldLookInput(out Vector2 look) {
            if (Time.FrameIndex < HeldLookInputEndFrame) {
                look = HeldLookInput;
                return true;
            }

            look = Vector2.Zero;
            return false;
        }

        public static bool TryConsumeToggleCrouch() {
            if (!QueuedToggleCrouch) {
                return false;
            }

            QueuedToggleCrouch = false;
            return true;
        }

        public static bool TryConsumeInteractRay(out Ray3 ray) {
            if (QueuedInteractRay.HasValue) {
                ray = QueuedInteractRay.Value;
                QueuedInteractRay = null;
                return true;
            }

            ray = default;
            return false;
        }

        public static bool TryConsumeHitRay(out Ray3 ray) {
            if (QueuedHitRay.HasValue) {
                ray = QueuedHitRay.Value;
                QueuedHitRay = null;
                return true;
            }

            ray = default;
            return false;
        }

        public static bool TryGetHeldDigRay(out Ray3 ray) {
            if (HeldDigRay.HasValue && Time.FrameIndex < HeldDigRayEndFrame) {
                ray = HeldDigRay.Value;
                return true;
            }

            HeldDigRay = null;
            ray = default;
            return false;
        }

        public static bool TryGetHeldAimRay(out Ray3 ray) {
            if (HeldAimRay.HasValue && Time.FrameIndex < HeldAimRayEndFrame) {
                ray = HeldAimRay.Value;
                return true;
            }

            HeldAimRay = null;
            ray = default;
            return false;
        }

        public static bool TryConsumeDrop() {
            if (!QueuedDrop) {
                return false;
            }

            QueuedDrop = false;
            return true;
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
            if (__instance.m_componentPlayer?.PlayerData?.IsMainPlayer != true) {
                return;
            }

            if (InputHijackState.TryGetHeldMoveInput(out Vector3 move)) {
                __instance.m_playerInput.Move = move;
                __instance.m_playerInput.CrouchMove = move;
                __instance.m_playerInput.CameraMove = move;
                __instance.m_playerInput.CameraCrouchMove = move;
            }
            if (InputHijackState.TryGetHeldLookInput(out Vector2 look)) {
                __instance.m_playerInput.Look = look;
            }
            if (InputHijackState.TryConsumeToggleCrouch()) {
                __instance.m_playerInput.ToggleCrouch = true;
            }

            if (InputHijackState.TryConsumeInteractRay(out Ray3 interactRay)) {
                __instance.m_playerInput.Interact = interactRay;
            }
            if (InputHijackState.TryConsumeHitRay(out Ray3 hitRay)) {
                __instance.m_playerInput.Hit = hitRay;
            }
            if (InputHijackState.TryGetHeldDigRay(out Ray3 digRay)) {
                __instance.m_playerInput.Dig = digRay;
            }
            if (InputHijackState.TryGetHeldAimRay(out Ray3 aimRay)) {
                __instance.m_playerInput.Aim = aimRay;
                InputHijackState.RecordAimInjection();
            }
            if (InputHijackState.TryConsumeDrop()) {
                __instance.m_playerInput.Drop = true;
            }
        }
    }
}
