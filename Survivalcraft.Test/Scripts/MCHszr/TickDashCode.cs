var loader = ModsManager.ModLoaders.FirstOrDefault(l => l is MCHszr.UnifiedMCHszrModLoader);
if (loader == null) return "no-loader";
var method = loader.GetType().GetMethod("UpdateDashing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (method == null) return "no-method";
var lastFrameField = loader.GetType().GetField("m_lastFrameTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (lastFrameField != null) {
    lastFrameField.SetValue(loader, Time.FrameStartTime);
}
try {
    method.Invoke(loader, new object[] { null });
} catch (System.Reflection.TargetInvocationException ex) {
    return "exception:" + (ex.InnerException?.GetType().Name ?? ex.GetType().Name);
}
return "ticked";