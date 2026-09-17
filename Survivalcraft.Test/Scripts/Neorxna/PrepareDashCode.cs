var windowType = typeof(Engine.Window);
var stateType = windowType.GetNestedType("State", System.Reflection.BindingFlags.NonPublic);
var field = windowType.GetField("m_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
field.SetValue(null, System.Enum.ToObject(stateType, 2));

foreach (var setting in Neorxna.NeorxnaSettingOptions.SettingConfigs.Values) {
    if (setting.Type == typeof(bool)) {
        Neorxna.NeorxnaSettingOptions.Settings.SetValue<object>(setting.Key, true);
    }
}

var loader = ModsManager.ModLoaders.FirstOrDefault(l => l.GetType().Assembly.GetName().Name == "neorxna");
if (loader == null) return "no-loader";

string dashKey = null;
foreach (var item in loader.GetKeyboardMappings()) {
    if (item.Value is Engine.Input.Key key && key == Engine.Input.Key.Null) {
        dashKey = item.Key;
        break;
    }
}
if (dashKey == null) return "no-dash-key";

SettingsManager.SetKeyboardMapping(dashKey, Engine.Input.Key.F);
return "ok:" + dashKey;
