using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Engine;

namespace Game.NetWork.Packages
{
    public class GameBalancerPackage : IPackage
    {
        public byte ID => (byte)PackageType.GameBalancer;
        public Client To { get; set; }
        public Client Except { get; set; }
        public Client From { get; set; }
        public ClientState MinNeedState => ClientState.NotConnected;

        private string ServerIp;

        public GameBalancerPackage() { }
        public GameBalancerPackage(string serverIp)
        {
            ServerIp = serverIp;
        }

        public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
        {
            if (ServerIp is null || isServer) return;

            if (!CommonLib.Resolve(ServerIp, out IPEndPoint ipendPoint)) return;

            CommonLib.Net.Stop(); // 仅置 isStopping
            var target = ipendPoint; // 解析好的地址
            Action handler = null;
            handler = () => {
                if (netNode.CurrentStage == NetNode.Stage.NotConnected)
                {
                    Window.Frame -= handler;
                    // 再切换 GameLoading 并连接
                    var parameters = new object[4];
                    parameters[2] = ipendPoint;
                    DialogsManager.HideAllDialogs();

                    var currentScreen = ScreensManager.CurrentScreen;
                    if (currentScreen is GameLoadingScreen loadingScreen)
                    {
                        loadingScreen.Leave();
                        loadingScreen.Enter(parameters);
                    }
                    else
                    {
                        ScreensManager.SwitchScreen("GameLoading", parameters);
                    }
                }
            };
            Window.Frame += handler;
        }

        public void ReadData(PackageStreamReader reader)
        {
            ServerIp = reader.ReadString();
        }

        public void WriteData(PackageStreamWriter writer)
        {
            writer.Write(ServerIp);
        }
    }
}
