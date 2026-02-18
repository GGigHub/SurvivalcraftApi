using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game.NetWork;
using Game.NetWork.Packages;
using static Game.NetWork.NetNode;

namespace Game;

public class GameLoadingScreen : Screen {
    public WorldInfo m_worldInfo;
    public string m_worldSnapshotName;
    public StateMachine m_stateMachine = new StateMachine();
    public static string fName = "GameLoadingScreen";
    public UnSeasonSpawnDialog spawnDialog = new UnSeasonSpawnDialog();
    private IPEndPoint m_serverEndPoint;
    public byte[] m_projectXmlData;
    public byte[] m_blockTextureData;
    public bool IsServerReply;
    public bool existBlockTexture;
    private string m_password;
    private bool IsAdventureRestart;
    private Stopwatch timer;

    public GameLoadingScreen() {
        XElement node = ContentManager.Get<XElement>("Screens/GameLoadingScreen");
        LoadContents(this, node);
        m_stateMachine.AddState("WaitingForFadeIn", null, delegate {
            if (!ScreensManager.IsAnimating) {
                if (string.IsNullOrEmpty(m_worldSnapshotName)) {
                    SetSpwanDialogMessage("连接服务器", 0.2f);
                    if (m_serverEndPoint != null) {
                        CommonLib.Net.ConnectServer(m_serverEndPoint, m_password);
                        DialogsManager.ShowDialog(this, spawnDialog);
                        m_stateMachine.TransitionTo("WaitServerReply");
                        //客户端开启包处理
                        CommonLib.Net.OnRecieve += Handle;
                    }
                    else {
                        m_stateMachine.TransitionTo("Loading");
                    }
                }
                else {
                    m_stateMachine.TransitionTo("RestoringSnapshot");
                }
            }
        }, null);

        m_stateMachine.AddState("WaitServerReply", delegate {
            timer = Stopwatch.StartNew();
        }, delegate {
            if (!string.IsNullOrEmpty(CommonLib.ModTransferState.Error)) {
                SetSpwanDialogMessage($"模组下载失败: {CommonLib.ModTransferState.Error}", 0.25f);
                CommonLib.Net.Stop(CommonLib.ModTransferState.Error);
            }
            if (CommonLib.ModTransferState.InProgress) {
                string msg = $"等待模组 {CommonLib.ModTransferState.Done}/{CommonLib.ModTransferState.Total}";
                if (!string.IsNullOrEmpty(CommonLib.ModTransferState.CurrentMod)) {
                    msg += $" [{CommonLib.ModTransferState.CurrentMod}]";
                    if (CommonLib.ModTransferState.CurrentFileTotalBytes > 0) {
                        msg += $" {CommonLib.ModTransferState.CurrentFileReceivedBytes / 1024}/{CommonLib.ModTransferState.CurrentFileTotalBytes / 1024}KB";
                    }
                }

                float p = 0.25f;
                if (CommonLib.ModTransferState.Total > 0) {
                    float cur = 0f;
                    if (CommonLib.ModTransferState.CurrentFileTotalBytes > 0) {
                        cur = CommonLib.ModTransferState.CurrentFileReceivedBytes / (float)CommonLib.ModTransferState.CurrentFileTotalBytes;
                        if (cur < 0f) cur = 0f;
                        if (cur > 1f) cur = 1f;
                    }
                    p = (CommonLib.ModTransferState.Done + cur) / (float)CommonLib.ModTransferState.Total;
                    if (p < 0.05f) p = 0.05f;
                    if (p > 0.95f) p = 0.95f;
                }

                SetSpwanDialogMessage(msg, p);
            }
            else {
                SetSpwanDialogMessage("等待服务器响应", 0.25f);
            }
            //Fix me::客户端中断连接口再次连接会卡住
            if (CommonLib.Net.CurrentStage == Stage.WaitForClientList) {
                if (!CommonLib.ModTransferState.InProgress && timer.ElapsedMilliseconds >= CommonLib.DISCONNECT_TIMEOUT) {
                    CommonLib.Net.Stop("连接超时");
                }
            }
            else if (CommonLib.Net.CurrentStage == Stage.Connected) {
                m_stateMachine.TransitionTo("Loading");
            }
            else {
                CommonLib.Net.Stop("连接断开");
            }
        }, delegate {
            if (CommonLib.Net.CurrentStage == Stage.Connected) {
                CommonLib.Net.QueuePackage(new ClientPackage(CommonLib.Net.Self.ID, ClientState.Connected));
            }
        });
        m_stateMachine.AddState("Loading", null,
        delegate {
            if (m_serverEndPoint != null) {
                SetSpwanDialogMessage("等待Project和材质", 0.4f);
                if (IsServerReply) {
                    m_stateMachine.TransitionTo("LoadServerReply");
                }
                if (!CommonLib.Net.IsConnected) {
                    CommonLib.Net.Stop("与服务器连接断开");
                }
            }
            else {
                ContainerWidget gamesWidget = ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");
                GameManager.LoadProject(m_worldInfo, gamesWidget);
                if (IsAdventureRestart && CommonLib.WorkType == WorkType.Client) {
                    CommonLib.Net.QueuePackage(new ComponentPlayerPackage(CommonLib.MainPlayer, ComponentPlayerPackage.PlayerAction.IntoPlaying));
                }
                ScreensManager.SwitchScreen("Game");
            }
        }, delegate {
            IsServerReply = false;
        });
        m_stateMachine.AddState("LoadServerReply", null, delegate {
            SetSpwanDialogMessage("加载服务器Project", 0.6f);
            if (CommonLib.Net.CurrentStage == Stage.Connected) {
                GameManager.LoadProject(m_projectXmlData, ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget"));
                ScreensManager.SwitchScreen("Game");
                m_projectXmlData = null;
                m_blockTextureData = null;
                IsServerReply = false;
                existBlockTexture = false;
            }
        }, null);


        m_stateMachine.AddState("RestoringSnapshot", null, delegate {
            GameManager.DisposeProject();
            WorldsManager.RestoreWorldFromSnapshot(m_worldInfo.DirectoryName, m_worldSnapshotName);
            m_stateMachine.TransitionTo("Loading");
        }, null);
    }

    public void Handle(NetNode node, IEnumerable<IPackage> packages) {
        foreach (var p in packages) {
            if (p is ClientPackage || p is ProjectPackage || p is ModsManifestPackage || p is ModsNeedPackage || p is ModsChunkPackage || p is ModsDonePackage) {
                try {
                    p.Handle(null, node, false);
                }
                catch (Exception e) {
                    Log.Error(string.Format("[{0}]{1}", p.GetType().Name, e.Message));
                }
            }
            else {
                //无法处理的包在Project加载后进行处理
                CommonLib.Net.AddPendingHanldePackage(p);
            }
            OriginalCommunityContentScreen originalCommunityContentScreen = ScreensManager.FindScreen<OriginalCommunityContentScreen>("OriginalCommunityContent");
            if (originalCommunityContentScreen != null) {
                originalCommunityContentScreen.m_listPanel.ClearItems();
                originalCommunityContentScreen.m_listPanel.ScrollPosition = 0f;
                originalCommunityContentScreen.m_itemsCacheExpiryTime = 0;
            }
        }
    }

    public void ReplyCall(bool hasTexure, byte[] textureData, byte[] projectData) {
        CommonLib.Net.OnRecieve -= Handle;
        IsServerReply = true;
        if (hasTexure) {
            if (CommonLib.BlockTexture != null) CommonLib.BlockTexture.Dispose();
            CommonLib.BlockTexture = Texture2D.Load(Image.Load(new MemoryStream(textureData)));
        }
        m_projectXmlData = projectData;
    }

    private StringBuilder builder = new StringBuilder();

    public void SetSpwanDialogMessage(string msg, float Progress) {
        builder.Clear();
        builder.Append(msg);
        int d = (int)Time.RealTime;
        d %= 4;
        switch (d) {
            case 1:
                builder.Append(".");
                break;
            case 2:
                builder.Append("..");
                break;
            case 3:
                builder.Append("...");
                break;
        }
        spawnDialog.LargeMessage = builder.ToString();
        spawnDialog.Progress = Progress;
    }
    public override void Update() {
        if (Input.Back || Input.Cancel) ScreensManager.SwitchScreen(ScreensManager.PreviousScreen);
        try {
            GameManager.UpdateProject();
            m_stateMachine.Update();
        }
        catch (Exception e) {
            Log.Error(e);
            ScreensManager.SwitchScreen(ScreensManager.PreviousScreen);
            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get(fName, 1), ExceptionManager.MakeFullErrorMessage(e), LanguageControl.Get("Usual", "ok"), null, null));
        }
    }
    public override void Leave() {
        //if (m_serverEndPoint != null && CommonLib.GetCurrentWorkType() == WorkType.Local) CommonLib.Net.StopImmediate();
        DialogsManager.HideDialog(spawnDialog);
    }

    public override void Enter(object[] parameters) {
        if (CommonLib.WorkType == WorkType.Local) {
            CommonLib.Net.StartLocal();
        }
        if (parameters.Length < 3) {
            m_worldInfo = (WorldInfo)parameters[0];
            m_worldSnapshotName = (string)parameters[1];
            m_serverEndPoint = null;
            IsAdventureRestart = m_worldSnapshotName == "AdventureRestart";
        }
        else {
            m_worldSnapshotName = null;
            m_serverEndPoint = (IPEndPoint)parameters[2];
            if (parameters.Length == 4) m_password = (string)parameters[3];

            CommonLib.ModTransferState.TargetServer = m_serverEndPoint?.ToString() ?? string.Empty;
            CommonLib.ModTransferState.TargetPassword = m_password ?? string.Empty;
        }
        m_stateMachine.TransitionTo("WaitingForFadeIn");
        ProgressManager.UpdateProgress("Loading World", 0f);
    }
}
