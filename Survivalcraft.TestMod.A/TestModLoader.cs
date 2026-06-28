using System;
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

namespace Survivalcraft.TestMod.A {
    public class TestModLoader : ModLoader {
        private UdpClient udpServer;
        private CancellationTokenSource cts = new();
        private readonly Queue<string> pendingWidgetClicks = new();
        private int lastWidgetClickInjectionFrame = -1;

        public override void __ModInitialize() {
            // Apply Harmony patches to hijack input
            var harmony = new Harmony("com.survivalcraft.testmod.a");
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

            SettingsManager.LetRamdomStatic = true;
            SettingsManager.MultithreadedTerrainUpdate = false;

            // Start UDP Server
            StartUdpServer();
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
                Log.Error($"[Survivalcraft.TestMod.A] Failed to start UDP Server: {ex.Message}");
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
                }
                Log.Information($"Command executed: {command}");
            }
            catch (Exception ex) {
                Log.Error($"[Survivalcraft.TestMod.A] Error processing command '{command}': {ex.Message}");
            }
        }

        private static object ExecuteCommandScript(string code) {
            var options = ScriptOptions.Default
                .WithReferences(typeof(GameManager).Assembly, typeof(Vector3).Assembly)
                .WithImports("System", "System.Linq", "System.Collections.Generic", "Engine", "Game");

            return CSharpScript.EvaluateAsync(code, options).GetAwaiter().GetResult();
        }

        private Widget FindWidgetByTextOrName(Widget root, string query) {
            if (root == null) return null;

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
            if (target == null
                || FindClickableWidget(target) != clickableWidget) {
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
            Widget target = FindWidgetByTextOrName(ScreensManager.CurrentScreen, query);
            if (target == null) {
                pendingWidgetClicks.Dequeue();
                Log.Information($"Command executed: {command} (Not Found)");
                return;
            }

            if (FindClickableWidget(target) == null) {
                pendingWidgetClicks.Dequeue();
                Log.Information($"Command executed: {command} (Not Clickable)");
                return;
            }
        }

        private static ClickableWidget FindClickableWidget(Widget widget) {
            if (widget == null) return null;
            if (widget is ClickableWidget clickableWidget) return clickableWidget;
            if (widget is BevelledButtonWidget bevelledButtonWidget) return bevelledButtonWidget.m_clickableWidget;
            if (widget is BitmapButtonWidget bitmapButtonWidget) return bitmapButtonWidget.m_clickableWidget;
            if (widget is CheckboxWidget checkboxWidget) return checkboxWidget.m_clickableWidget;
            if (widget is LinkWidget linkWidget) return linkWidget.m_clickableWidget;
            return widget is ContainerWidget containerWidget
                ? containerWidget.Children.Find<ClickableWidget>(false)
                : null;
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
            base.ModDispose();
        }
    }
}
