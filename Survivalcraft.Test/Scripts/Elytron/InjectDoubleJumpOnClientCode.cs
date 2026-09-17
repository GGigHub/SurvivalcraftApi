var project = Game.GameManager.Project;
if (project == null) return "no-project";
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
var player = playerData?.ComponentPlayer;
if (player == null) return "no-player";
var gliding = player.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Elytron.ComponentGliding");
if (gliding == null) return "no-gliding";
var locomotion = player.ComponentLocomotion;
if (locomotion == null) return "no-locomotion";

var rtType = System.AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => {
        try { return a.GetTypes(); } catch { return new System.Type[0]; }
    })
    .FirstOrDefault(t => t.FullName == "Game.ElytronCompatNetRuntime");
if (rtType == null) return "no-runtime";
var capture = rtType.GetMethod("CaptureJumpIntent");
if (capture == null) return "no-capture-method";

// 先清空运行时边沿状态
locomotion.JumpOrder = 0f;
capture.Invoke(null, new object[] { gliding });

// 第一次跳跃边沿（向服务端发送第 1 个跳越意图）
locomotion.JumpOrder = 1f;
capture.Invoke(null, new object[] { gliding });
locomotion.JumpOrder = 0f;
capture.Invoke(null, new object[] { gliding });

// 间隔片刻（大于服务端最小包间隔 0.02s，小于双击窗口 0.3s）
System.Threading.Thread.Sleep(120);

// 第二次跳跃边沿（双击 → 服务端切换滑翔）
locomotion.JumpOrder = 1f;
capture.Invoke(null, new object[] { gliding });
locomotion.JumpOrder = 0f;
capture.Invoke(null, new object[] { gliding });

string ready = "unknown";
var coreType = System.AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => {
        try { return a.GetTypes(); } catch { return new System.Type[0]; }
    })
    .FirstOrDefault(t => t.FullName == "Game.CompatNetCore");
if (coreType != null) {
    var prop = coreType.GetProperty("NetworkBridgeReady");
    object r = prop != null ? prop.GetValue(null, null) : null;
    ready = r != null ? r.ToString() : "null";
}
return "injected:bridgeReady=" + ready;