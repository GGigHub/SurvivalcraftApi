using Godot;

namespace SurvivalcraftGodot;

public static class InputSetup
{
    public static void Ensure()
    {
        AddKey("move_forward", Key.W);
        AddKey("move_back", Key.S);
        AddKey("move_left", Key.A);
        AddKey("move_right", Key.D);
        AddKey("jump", Key.Space);
        AddKey("sneak", Key.Shift);
        AddKey("drop_item", Key.Q);
        AddKey("open_inventory", Key.I);
        AddKey("pause", Key.Escape);
        AddKey("hotbar_1", Key.Key1);
        AddKey("hotbar_2", Key.Key2);
        AddKey("hotbar_3", Key.Key3);
        AddKey("hotbar_4", Key.Key4);
        AddKey("hotbar_5", Key.Key5);
        AddKey("hotbar_6", Key.Key6);
        AddKey("hotbar_7", Key.Key7);
        AddKey("hotbar_8", Key.Key8);
        AddKey("hotbar_9", Key.Key9);
        AddKey("hotbar_0", Key.Key0);
        AddMouse("attack", MouseButton.Left);
        AddMouse("use", MouseButton.Right);
    }

    static void AddKey(string action, Key key)
    {
        EnsureAction(action);
        if (InputMap.ActionGetEvents(action).Count > 0)
        {
            return;
        }

        var ev = new InputEventKey { PhysicalKeycode = key };
        InputMap.ActionAddEvent(action, ev);
    }

    static void AddMouse(string action, MouseButton button)
    {
        EnsureAction(action);
        if (InputMap.ActionGetEvents(action).Count > 0)
        {
            return;
        }

        var ev = new InputEventMouseButton { ButtonIndex = button };
        InputMap.ActionAddEvent(action, ev);
    }

    static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }
    }
}
