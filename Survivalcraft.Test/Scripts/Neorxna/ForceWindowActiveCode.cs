var windowType = typeof(Engine.Window);
var stateType = windowType.GetNestedType("State", System.Reflection.BindingFlags.NonPublic);
var field = windowType.GetField("m_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
bool wasActive = Engine.Window.IsActive;
field.SetValue(null, System.Enum.ToObject(stateType, 2));
return "wasActive=" + wasActive;
