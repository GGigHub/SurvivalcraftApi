using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using Engine.Input;
using HarmonyLib;
using Game;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Survivalcraft.TestAutomation {
    public partial class TestAutomationModLoader : ModLoader {
        private UdpClient udpServer;
        private CancellationTokenSource cts = new();
        private readonly Queue<string> pendingWidgetClicks = new();
        private int lastWidgetClickInjectionFrame = -1;

        public override void __ModInitialize() {
            // Apply Harmony patches to hijack input
            var harmony = new Harmony("com.survivalcraft.testautomation");
            harmony.PatchAll();
            ModsManager.RegisterHook("BeforeWidgetUpdate", this);
            ModsManager.RegisterHook("AfterWidgetUpdate", this);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--game-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) {
                    SettingsManager.ServerPort = p;
                }
            }

            // 吵到我听歌了
            SettingsManager.MusicVolume = 0;
            SettingsManager.SoundsVolume = 0;
            SettingsManager.FileAssociationEnabled = false;

            SettingsManager.LetRamdomStatic = true;
            SettingsManager.MultithreadedTerrainUpdate = false;

            InitializeTestModChannel();
            InitializeTypedPlayerActions();

            // Start UDP Server
            StartUdpServer();
            StartTypedControlServer();
        }

        private void StartUdpServer() {
            int cmdPort = 23944;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--cmd-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) {
                    cmdPort = p;
                }
            }
            try {
                udpServer = new UdpClient(cmdPort);

                Task.Run(async () => {
                    while (!cts.IsCancellationRequested) {
                        try {
                            var receiveResult = await udpServer.ReceiveAsync(cts.Token);
                            string message = Encoding.UTF8.GetString(receiveResult.Buffer).Trim();
                            var remoteEP = receiveResult.RemoteEndPoint;

                            // Process message on the main thread via Dispatcher
                            Dispatcher.Dispatch(() => {
                                ProcessCommand(message, remoteEP);
                            });
                        }
                        catch (OperationCanceledException) {
                            break;
                        }
                    }
                }, cts.Token);
            }
            catch (Exception ex) {
                Log.Error($"[Survivalcraft.TestAutomation] Failed to start UDP Server: {ex.Message}");
            }
        }

        private void ProcessCommand(string command, IPEndPoint remoteEP) {
            string[] parts = command.Split(' ');
            if (parts.Length == 0) return;

            string action = parts[0].ToUpper();
            try {
                if (action == "KEYDOWN" && parts.Length >= 2) {
                    if (Enum.TryParse<Key>(parts[1], true, out Key key)) {
                        Keyboard.ProcessKeyDown(key);
                    }
                }
                else if (action == "KEYUP" && parts.Length >= 2) {
                    if (Enum.TryParse<Key>(parts[1], true, out Key key)) {
                        Keyboard.ProcessKeyUp(key);
                    }
                }
                else if (action == "MOUSEMOVE" && parts.Length >= 3) {
                    int x = int.Parse(parts[1]);
                    int y = int.Parse(parts[2]);
                    InputHijackState.IsInjectingMouseMove = true;
                    try {
                        Mouse.ProcessMouseMove(new Point2(x, y));
                    }
                    finally {
                        InputHijackState.IsInjectingMouseMove = false;
                    }
                }
                else if (action == "MOUSEDELTA" && parts.Length >= 3) {
                    int x = int.Parse(parts[1]);
                    int y = int.Parse(parts[2]);
                    InputHijackState.QueuedMouseMovement = new Point2(x, y);
                }
                else if (action == "MOVE_INPUT" && parts.Length >= 5) {
                    float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    int frameCount = int.Parse(parts[4], CultureInfo.InvariantCulture);
                    InputHijackState.HoldMoveInput(new Vector3(x, y, z), frameCount);
                }
                else if (action == "INTERACT_RAY" && parts.Length >= 7) {
                    float px = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float py = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float pz = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float dx = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float dy = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float dz = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    InputHijackState.QueueInteractRay(new Ray3(new Vector3(px, py, pz), new Vector3(dx, dy, dz)));
                }
                else if (action == "HIT_RAY" && parts.Length >= 7) {
                    float px = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float py = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float pz = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float dx = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float dy = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float dz = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    InputHijackState.QueueHitRay(new Ray3(new Vector3(px, py, pz), new Vector3(dx, dy, dz)));
                }
                else if (action == "DIG_RAY" && parts.Length >= 8) {
                    float px = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float py = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float pz = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float dx = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float dy = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float dz = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    int frameCount = int.Parse(parts[7], CultureInfo.InvariantCulture);
                    InputHijackState.HoldDigRay(new Ray3(new Vector3(px, py, pz), new Vector3(dx, dy, dz)), frameCount);
                }
                else if (action == "AIM_RAY" && parts.Length >= 8) {
                    float px = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float py = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float pz = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float dx = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float dy = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float dz = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    int frameCount = int.Parse(parts[7], CultureInfo.InvariantCulture);
                    InputHijackState.HoldAimRay(new Ray3(new Vector3(px, py, pz), new Vector3(dx, dy, dz)), frameCount);
                }
                else if (action == "DROP_ONCE") {
                    InputHijackState.QueueDrop();
                }
                else if (action == "MOUSEDOWN" && parts.Length >= 4) {
                    if (Enum.TryParse<MouseButton>(parts[1], true, out MouseButton btn)) {
                        int x = int.Parse(parts[2]);
                        int y = int.Parse(parts[3]);
                        Mouse.ProcessMouseDown(btn, new Point2(x, y));
                    }
                }
                else if (action == "MOUSEUP" && parts.Length >= 4) {
                    if (Enum.TryParse<MouseButton>(parts[1], true, out MouseButton btn)) {
                        int x = int.Parse(parts[2]);
                        int y = int.Parse(parts[3]);
                        Mouse.ProcessMouseUp(btn, new Point2(x, y));
                    }
                }
                else if (action == "CLICK_WIDGET" && parts.Length >= 2) {
                    pendingWidgetClicks.Enqueue(command);
                    return;
                }
                else if (action == "FUNC" && parts.Length >= 2) {
                    string code = string.Join(" ", parts, 1, parts.Length - 1);
                    try {
                        object result = ExecuteCommandScript(code);
                        string json = System.Text.Json.JsonSerializer.Serialize(result);
                        byte[] bytes = Encoding.UTF8.GetBytes("OK:" + json);
                        udpServer.Send(bytes, bytes.Length, remoteEP);
                    }
                    catch (Exception ex) {
                        byte[] bytes = Encoding.UTF8.GetBytes("ERROR:" + ex.Message);
                        udpServer.Send(bytes, bytes.Length, remoteEP);
                        throw;
                    }
                    Log.Information($"Command executed: {command}");
                    return;
                }
                else if (action == "EXEC" && parts.Length >= 2) {
                    string code = string.Join(" ", parts, 1, parts.Length - 1);
                    ExecuteCommandScript(code);
                    byte[] bytes = Encoding.UTF8.GetBytes("OK:null");
                    udpServer.Send(bytes, bytes.Length, remoteEP);
                }
                Log.Information($"Command executed: {command}");
            }
            catch (Exception ex) {
                Log.Error($"[Survivalcraft.TestAutomation] Error processing command '{command}': {ex.Message}");
            }
        }

        private static object ExecuteCommandScript(string code) {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Concat(ModsManager.Dlls.Values)
                .Concat(ModsManager.ModLoaders.Select(l => l.GetType().Assembly))
                .Concat(ModsManager.ModList.SelectMany(m => m.GetAssemblies()))
                .Where(a => !a.IsDynamic)
                .Distinct();

            var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
            foreach (var assembly in assemblies) {
                try {
                    if (!string.IsNullOrEmpty(assembly.Location)) {
                        references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(assembly.Location));
                    }
                    else {
                        unsafe {
                            if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* blob, out int length)) {
                                var moduleMetadata = Microsoft.CodeAnalysis.ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                                references.Add(Microsoft.CodeAnalysis.AssemblyMetadata.Create(moduleMetadata).GetReference());
                            }
                        }
                    }
                }
                catch {
                    // ignore unresolvable assemblies
                }
            }

            var options = ScriptOptions.Default
                .WithReferences(references)
                .WithImports("System", "System.Linq", "System.Collections.Generic", "Engine", "Game");

            return CSharpScript.EvaluateAsync(code, options).GetAwaiter().GetResult();
        }

        private Widget FindWidgetByTextOrName(Widget root, string query) {
            if (root == null) return null;
            if (!root.IsVisible || !root.IsEnabled) return null;

            if (string.Equals(root.Name, query, StringComparison.OrdinalIgnoreCase)) return root;
            if (root is ButtonWidget button && string.Equals(button.Text, query, StringComparison.OrdinalIgnoreCase)) return button;
            if (root is LabelWidget label && string.Equals(label.Text, query, StringComparison.OrdinalIgnoreCase)) return label;

            if (root is ContainerWidget container) {
                foreach (var child in container.Children) {
                    var found = FindWidgetByTextOrName(child, query);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public override void AfterWidgetUpdate(Widget widget) {
            if (pendingWidgetClicks.Count == 0
                || lastWidgetClickInjectionFrame == Time.FrameIndex
                || widget is not ClickableWidget clickableWidget) {
                return;
            }

            string command = pendingWidgetClicks.Peek();
            string query = command.Substring(command.IndexOf(' ') + 1);
            Widget target = FindWidgetByTextOrName(ScreensManager.CurrentScreen, query);
            ClickableWidget targetClickableWidget = FindClickableWidget(target);
            if (targetClickableWidget != clickableWidget) {
                return;
            }

            InjectClick(clickableWidget);
            pendingWidgetClicks.Dequeue();
            lastWidgetClickInjectionFrame = Time.FrameIndex;
            Log.Information($"Command executed: {command}");
        }

        public override void BeforeWidgetUpdate(Widget widget) {
            if (pendingWidgetClicks.Count == 0
                || lastWidgetClickInjectionFrame == Time.FrameIndex
                || widget != ScreensManager.CurrentScreen) {
                return;
            }

            string command = pendingWidgetClicks.Peek();
            string query = command.Substring(command.IndexOf(' ') + 1);
            _ = FindWidgetByTextOrName(ScreensManager.CurrentScreen, query);
        }

        private static ClickableWidget FindClickableWidget(Widget widget) {
            if (widget == null) return null;
            if (widget is ClickableWidget clickableWidget) return clickableWidget;
            if (widget is BevelledButtonWidget bevelledButtonWidget) return bevelledButtonWidget.m_clickableWidget;
            if (widget is BitmapButtonWidget bitmapButtonWidget) return bitmapButtonWidget.m_clickableWidget;
            if (widget is CheckboxWidget checkboxWidget) return checkboxWidget.m_clickableWidget;
            if (widget is LinkWidget linkWidget) return linkWidget.m_clickableWidget;
            if (widget is ContainerWidget containerWidget) {
                ClickableWidget childClickableWidget = containerWidget.Children.Find<ClickableWidget>(false);
                if (childClickableWidget != null) return childClickableWidget;
            }
            for (ContainerWidget parent = widget.ParentWidget; parent != null; parent = parent.ParentWidget) {
                if (parent is BevelledButtonWidget parentBevelledButtonWidget) return parentBevelledButtonWidget.m_clickableWidget;
                if (parent is BitmapButtonWidget parentBitmapButtonWidget) return parentBitmapButtonWidget.m_clickableWidget;
                if (parent is CheckboxWidget parentCheckboxWidget) return parentCheckboxWidget.m_clickableWidget;
                if (parent is LinkWidget parentLinkWidget) return parentLinkWidget.m_clickableWidget;
            }
            return null;
        }

        private static void InjectClick(ClickableWidget clickableWidget) {
            clickableWidget.IsPressed = false;
            clickableWidget.IsTapped = true;
            clickableWidget.IsClicked = true;
            if (clickableWidget.IsAutoCheckingEnabled) {
                clickableWidget.IsChecked = !clickableWidget.IsChecked;
            }
            if (!string.IsNullOrEmpty(clickableWidget.SoundName)) {
                AudioManager.PlaySound(clickableWidget.SoundName, 1f, 0f, 0f);
            }
        }

        public override void ModDispose() {
            cts.Cancel();
            udpServer?.Close();
            StopTypedControlServer();
            DisposeTypedPlayerActions();
            DisposeTestModChannel();
            base.ModDispose();
        }
    }
}
