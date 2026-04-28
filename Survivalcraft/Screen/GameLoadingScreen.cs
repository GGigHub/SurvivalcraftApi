using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game.NetWork;
using Game.NetWork.Packages;
using Stage = Game.NetWork.NetworkStage;

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
                        NetworkManager.ConnectServer(m_serverEndPoint, m_password);
                        DialogsManager.ShowDialog(this, spawnDialog);
                        m_stateMachine.TransitionTo("WaitServerReply");
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
            SetSpwanDialogMessage("等待服务器响应", 0.25f);
            // Fix me: 客户端中断连接后再次连接可能会卡住。
            if (NetworkManager.CurrentStage == Stage.WaitForClientList) {
                if (timer.ElapsedMilliseconds >= NetworkManager.DISCONNECT_TIMEOUT) {
                    NetworkManager.Stop();
                }
            }
            else if (NetworkManager.CurrentStage == Stage.Connected) {
                m_stateMachine.TransitionTo("Loading");
            }
            else {
                NetworkManager.Stop();
            }
        }, delegate {
            if (NetworkManager.CurrentStage == Stage.Connected) {
                NetworkManager.QueuePackage(new ClientStateChangePackage(NetworkManager.Self.ID, ClientStage.Connected));
            }
        });
        m_stateMachine.AddState("Loading", null,
        delegate {
            if (m_serverEndPoint != null) {
                SetSpwanDialogMessage("等待Project和材质", 0.4f);
                if (IsServerReply) {
                    m_stateMachine.TransitionTo("LoadServerReply");
                }
                if (NetworkManager.CurrentStage != Stage.Connected) {
                    NetworkManager.Stop();
                }
            }
            else {
                ContainerWidget gamesWidget = ScreensManager.FindScreen<GameScreen>("Game").Children.Find<ContainerWidget>("GamesWidget");
                GameManager.LoadProject(m_worldInfo, gamesWidget);
                if (IsAdventureRestart && NetworkManager.WorkType == NetWork.WorkType.Client) {
                    NetworkManager.QueuePackage(new PlayerIntoPlayingPackage(NetworkManager.MainPlayer));
                }
                ScreensManager.SwitchScreen("Game");
            }
        }, delegate {
            IsServerReply = false;
        });
        m_stateMachine.AddState("LoadServerReply", null, delegate {
            SetSpwanDialogMessage("加载服务器 Project", 0.6f);
            if (NetworkManager.CurrentStage == Stage.Connected) {
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

    public void ReplyCall(bool hasTexure, byte[] textureData, byte[] projectData) {
        IsServerReply = true;
        if (hasTexure) {
            if (NetworkManager.BlockTexture != null) NetworkManager.BlockTexture.Dispose();
            NetworkManager.BlockTexture = Texture2D.Load(Image.Load(new MemoryStream(textureData)));
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
        //if (m_serverEndPoint != null && CommonLib.GetCurrentWorkType() == NetWork.WorkType.Local) CommonLib.Net.StopImmediate();
        DialogsManager.HideDialog(spawnDialog);
    }

    public override void Enter(object[] parameters) {
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
        }
        m_stateMachine.TransitionTo("WaitingForFadeIn");
        ProgressManager.UpdateProgress("Loading World", 0f);
    }
}
