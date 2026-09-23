using System.Text.Json.Nodes;
using Engine;

namespace Game {
    public class ModStudioMenuEntryWidget : CanvasWidget {
        public BevelledButtonWidget Button;

        public ModStudioMenuEntryWidget() {
            Button = new BevelledButtonWidget {
                Text = ModStudioScreen.T("MenuButton"),
                Size = new Vector2(200f, 52f),
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(0f, 4f)
            };
            Children.Add(Button);
        }

        public override void Update() {
            if (Button.IsClicked) {
                ScreensManager.SwitchScreen("ModStudio");
            }
        }
    }

    public class ModStudioScreen : Screen {
        public ListPanelWidget m_list;
        public LabelWidget m_detail;
        public LabelWidget m_log;
        public LabelWidget m_agent;
        public BevelledButtonWidget m_back;
        public BevelledButtonWidget m_new;
        public BevelledButtonWidget m_delete;
        public BevelledButtonWidget m_compile;
        public BevelledButtonWidget m_pack;
        public BevelledButtonWidget m_install;
        public BevelledButtonWidget m_uninstall;
        public BevelledButtonWidget m_reload;
        public BevelledButtonWidget m_copyToken;
        public bool m_busy;
        public string m_logText = "";

        public ModStudioScreen() {
            Children.Add(
                new RectangleWidget {
                    Size = new Vector2(float.PositiveInfinity),
                    FillColor = new Color(16, 18, 24),
                    OutlineColor = Color.Transparent,
                    OutlineThickness = 0f
                }
            );
            StackPanelWidget root = new() {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            Children.Add(root);
            root.Children.Add(BuildTop());
            root.Children.Add(BuildBody());
            root.Children.Add(BuildButtons());
            root.Children.Add(BuildLog());
            root.Children.Add(BuildAgentBar());
        }

        public static string T(string key) {
            string text = LanguageControl.Get("ModStudio", key);
            return string.IsNullOrEmpty(text) || text == "ModStudio:" + key ? Fallback(key) : text;
        }

        static string Fallback(string key) => key switch {
            "MenuButton" => "模组工坊",
            "Title" => "模组工坊",
            "Back" => "返回",
            "New" => "新建",
            "Delete" => "删除工作区",
            "Compile" => "编译",
            "Pack" => "打包",
            "Install" => "安装",
            "Uninstall" => "卸载",
            "Reload" => "重载游戏",
            "CopyToken" => "复制 Token",
            "CreateTitle" => "新建模组（显示名）",
            "EmptyList" => "还没有工作区。点「新建」开始。",
            "LogEmpty" => "日志会显示在这里。",
            "ConfirmDelete" => "删除工作区不会自动卸载已安装的 .scmod。确定删除？",
            "ConfirmReload" => "重载会回到加载界面并重新装载全部模组。继续？",
            "ConfirmUninstall" => "将删除 Mods 目录中的该模组包。继续？",
            "Busy" => "请等待当前操作完成。",
            "SelectFirst" => "请先选择一个项目。",
            "Created" => "已创建工作区。",
            "NeedSdk" => "未找到 dotnet。请安装 .NET 10 SDK。",
            "AgentOn" => "Agent 已启动",
            "AgentOff" => "Agent 未启动",
            _ => key
        };

        public override void Enter(object[] parameters) {
            StudioSettings.Load();
            RefreshList();
            RefreshAgent();
            if (string.IsNullOrEmpty(m_logText)) {
                SetLog(T("LogEmpty"));
            }
            base.Enter(parameters);
        }

        public override void Update() {
            if (Input.Back || Input.Cancel || m_back.IsClicked) {
                ScreensManager.SwitchScreen("MainMenu");
                return;
            }
            bool has = Selected() != null;
            m_delete.IsEnabled = has && !m_busy;
            m_compile.IsEnabled = has && !m_busy;
            m_pack.IsEnabled = has && !m_busy;
            m_install.IsEnabled = has && !m_busy;
            m_uninstall.IsEnabled = has && !m_busy;
            m_new.IsEnabled = !m_busy;
            m_reload.IsEnabled = !m_busy;
            if (m_list.SelectedItem != null) {
                UpdateDetail((StudioProjectInfo)m_list.SelectedItem);
            }
            if (m_new.IsClicked) {
                DialogsManager.ShowDialog(this, new TextBoxDialog(T("CreateTitle"), "HelloStudio", 40, result => {
                    if (string.IsNullOrWhiteSpace(result)) {
                        return;
                    }
                    RunTool("mod.create", new JsonObject { ["displayName"] = result.Trim() }, true);
                }));
            }
            if (m_delete.IsClicked) {
                Confirm(T("ConfirmDelete"), () => RunTool("mod.delete_workspace", WithPackage(new JsonObject { ["confirm"] = true }), true));
            }
            if (m_compile.IsClicked) {
                RunTool("mod.compile", WithPackage(), true);
            }
            if (m_pack.IsClicked) {
                RunTool("mod.pack", WithPackage(), true);
            }
            if (m_install.IsClicked) {
                RunTool("mod.install", WithPackage(), true);
            }
            if (m_uninstall.IsClicked) {
                Confirm(T("ConfirmUninstall"), () => RunTool("mod.uninstall", WithPackage(), true));
            }
            if (m_reload.IsClicked) {
                Confirm(T("ConfirmReload"), () => RunTool("game.reload", new JsonObject { ["confirm"] = true }, false));
            }
            if (m_copyToken.IsClicked) {
                ClipboardManager.ClipboardString = StudioSettings.Current.Token ?? "";
                SetLog("Token copied.\n" + StudioSettings.Current.BaseUrl);
            }
        }

        void RunTool(string name, JsonObject args, bool refresh) {
            if (m_busy) {
                DialogsManager.ShowDialog(this, new MessageDialog(T("Busy"), "", LanguageControl.Ok, null, _ => { }));
                return;
            }
            if (args == null) {
                DialogsManager.ShowDialog(this, new MessageDialog(T("SelectFirst"), "", LanguageControl.Ok, null, _ => { }));
                return;
            }
            m_busy = true;
            CancellableBusyDialog busy = new(T(name == "mod.compile" ? "Compile" : "Title"), false);
            DialogsManager.ShowDialog(this, busy);
            Task.Run(() => {
                StudioResult result = StudioToolService.Call(name, args);
                Dispatcher.Dispatch(() => {
                    m_busy = false;
                    DialogsManager.HideDialog(busy);
                    string log = ResultLog(result);
                    SetLog(log);
                    if (!result.Ok) {
                        DialogsManager.ShowDialog(this, new MessageDialog(result.Code ?? T("Title"), result.Message ?? log, LanguageControl.Ok, null, _ => { }));
                    }
                    else if (name == "mod.create") {
                        SetLog(T("Created") + "\n" + log);
                    }
                    else if (name is "mod.install" or "mod.uninstall") {
                        Confirm(T("ConfirmReload"), () => StudioModLifecycle.RequestReload());
                    }
                    if (refresh) {
                        string keep = StudioJson.GetString(args, "packageName");
                        if (string.IsNullOrEmpty(keep) && result.Data is JsonObject data) {
                            keep = StudioJson.GetString(data, "packageName");
                        }
                        RefreshList(keep);
                    }
                    RefreshAgent();
                });
            });
        }

        static string ResultLog(StudioResult result) {
            if (result.Data is JsonObject obj && obj.TryGetPropertyValue("log", out JsonNode logNode) && logNode != null) {
                return logNode.ToString();
            }
            return result.ToJson().ToJsonString();
        }

        JsonObject WithPackage(JsonObject extra = null) {
            StudioProjectInfo info = Selected();
            if (info == null) {
                return null;
            }
            JsonObject obj = extra ?? [];
            obj["packageName"] = info.PackageName;
            return obj;
        }

        StudioProjectInfo Selected() => m_list.SelectedItem as StudioProjectInfo;

        void Confirm(string message, Action action, Action cancelAction = null) {
            DialogsManager.ShowDialog(
                this,
                new MessageDialog(
                    T("Title"),
                    message,
                    LanguageControl.Yes,
                    LanguageControl.No,
                    button => {
                        if (button == MessageDialogButton.Button1) {
                            action();
                        }
                        else {
                            cancelAction?.Invoke();
                        }
                    }
                )
            );
        }

        void RefreshList(string selectPackage = null) {
            object keep = selectPackage;
            if (keep == null && Selected() != null) {
                keep = Selected().PackageName;
            }
            m_list.ClearItems();
            List<StudioProjectInfo> projects = StudioWorkspace.ListProjects();
            if (projects.Count == 0) {
                UpdateDetail(null);
            }
            foreach (StudioProjectInfo info in projects) {
                m_list.AddItem(info);
                if (keep is string pkg && pkg == info.PackageName) {
                    m_list.SelectedItem = info;
                }
            }
            if (m_list.SelectedItem == null && projects.Count > 0) {
                m_list.SelectedItem = projects[0];
            }
            UpdateDetail(Selected());
        }

        void UpdateDetail(StudioProjectInfo info) {
            if (info == null) {
                m_detail.Text = T("EmptyList");
                return;
            }
            bool installed = false;
            try {
                installed = StudioModLifecycle.IsInstalled(info.PackageName);
            }
            catch {
                // ignored
            }
            m_detail.Text =
                $"{info.DisplayName}\n" +
                $"{info.PackageName}\n" +
                $"Assembly: {info.AssemblyName}\n" +
                $"Compile: {(info.LastCompileOk ? "OK" : "-")} {info.LastCompileUtc}\n" +
                $"Pack: {info.LastPackUtc}\n" +
                $"Install: {info.LastInstallUtc}\n" +
                $"In Mods/: {(installed ? "yes" : "no")}";
        }

        void RefreshAgent() {
            string status = StudioHttpServer.Instance.IsRunning ? T("AgentOn") : T("AgentOff");
            if (!string.IsNullOrEmpty(StudioHttpServer.Instance.LastError)) {
                status += " — " + StudioHttpServer.Instance.LastError;
            }
            m_agent.Text = $"{status}  {StudioSettings.Current.BaseUrl}";
        }

        void SetLog(string text) {
            m_logText = text ?? "";
            if (m_logText.Length > 4000) {
                m_logText = m_logText.Substring(m_logText.Length - 4000);
            }
            m_log.Text = m_logText;
        }

        StackPanelWidget BuildTop() {
            StackPanelWidget bar = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Stretch,
                Margin = new Vector2(8f, 6f)
            };
            m_back = SmallButton(T("Back"), 140f);
            LabelWidget title = new() {
                Text = T("Title"),
                FontScale = 1.15f,
                Color = Color.White,
                VerticalAlignment = WidgetAlignment.Center,
                HorizontalAlignment = WidgetAlignment.Center
            };
            bar.Children.Add(m_back);
            bar.Children.Add(new CanvasWidget { Size = new Vector2(float.PositiveInfinity, 1f) });
            bar.Children.Add(title);
            bar.Children.Add(new CanvasWidget { Size = new Vector2(float.PositiveInfinity, 1f) });
            return bar;
        }

        StackPanelWidget BuildBody() {
            StackPanelWidget row = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            m_list = new ListPanelWidget {
                ItemSize = 52f,
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            m_list.ItemWidgetFactory = item => {
                StudioProjectInfo info = item as StudioProjectInfo;
                return new LabelWidget {
                    Text = info == null ? "" : info.DisplayName,
                    Color = Color.White,
                    VerticalAlignment = WidgetAlignment.Center,
                    Margin = new Vector2(8f, 4f)
                };
            };
            m_detail = new LabelWidget {
                Text = T("EmptyList"),
                Color = new Color(220, 220, 220),
                WordWrap = true,
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Vector2(12f, 8f)
            };
            CanvasWidget listBox = new() {
                Size = new Vector2(320f, 280f),
                Margin = new Vector2(8f, 4f)
            };
            listBox.Children.Add(m_list);
            CanvasWidget detailBox = new() {
                Size = new Vector2(float.PositiveInfinity, 280f),
                HorizontalAlignment = WidgetAlignment.Stretch
            };
            detailBox.Children.Add(m_detail);
            row.Children.Add(listBox);
            row.Children.Add(detailBox);
            return row;
        }

        StackPanelWidget BuildButtons() {
            StackPanelWidget row = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(4f, 4f)
            };
            m_new = SmallButton(T("New"), 110f);
            m_delete = SmallButton(T("Delete"), 150f);
            m_compile = SmallButton(T("Compile"), 110f);
            m_pack = SmallButton(T("Pack"), 110f);
            m_install = SmallButton(T("Install"), 110f);
            m_uninstall = SmallButton(T("Uninstall"), 110f);
            m_reload = SmallButton(T("Reload"), 130f);
            row.Children.Add(m_new);
            row.Children.Add(m_delete);
            row.Children.Add(m_compile);
            row.Children.Add(m_pack);
            row.Children.Add(m_install);
            row.Children.Add(m_uninstall);
            row.Children.Add(m_reload);
            return row;
        }

        StackPanelWidget BuildLog() {
            ScrollPanelWidget scroll = new() {
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            m_log = new LabelWidget {
                Text = T("LogEmpty"),
                Color = new Color(180, 200, 180),
                WordWrap = true,
                FontScale = 0.7f,
                HorizontalAlignment = WidgetAlignment.Stretch
            };
            scroll.Children.Add(m_log);
            CanvasWidget logBox = new() {
                Size = new Vector2(float.PositiveInfinity, 160f),
                HorizontalAlignment = WidgetAlignment.Stretch,
                Margin = new Vector2(8f, 4f)
            };
            logBox.Children.Add(scroll);
            StackPanelWidget wrap = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
            wrap.Children.Add(logBox);
            return wrap;
        }

        StackPanelWidget BuildAgentBar() {
            StackPanelWidget bar = new() {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Stretch,
                Margin = new Vector2(8f, 6f)
            };
            m_agent = new LabelWidget {
                Text = "",
                Color = new Color(160, 190, 230),
                FontScale = 0.75f,
                VerticalAlignment = WidgetAlignment.Center,
                HorizontalAlignment = WidgetAlignment.Stretch
            };
            m_copyToken = SmallButton(T("CopyToken"), 160f);
            bar.Children.Add(m_agent);
            bar.Children.Add(m_copyToken);
            return bar;
        }

        static BevelledButtonWidget SmallButton(string text, float width) => new() {
            Text = text,
            Size = new Vector2(width, 46f),
            Margin = new Vector2(3f, 2f)
        };
    }
}
