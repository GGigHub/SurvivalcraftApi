using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Game.NetWork;

class WebTime
{
    const string ntpServer = "ntp.aliyun.com";
    const byte serverReplyTime = 40;

    public static DateTime getWebTime()
    {
        try
        {
            byte[] ntpData = new byte[48];
            ntpData[0] = 0x1B; // LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)
            IPAddress[] addresses = Dns.GetHostEntry(ntpServer).AddressList;
            IPEndPoint ipEndPoint = new IPEndPoint(addresses[0], 123);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(ipEndPoint);
            socket.ReceiveTimeout = 10;
            socket.Send(ntpData);
            socket.Receive(ntpData);
            socket.Close();
            ulong intPart = BitConverter.ToUInt32(ntpData, serverReplyTime);
            ulong fractPart = BitConverter.ToUInt32(ntpData, serverReplyTime + 4);
            intPart = swapEndian(intPart);
            fractPart = swapEndian(fractPart);
            ulong milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000UL);
            DateTime webTime = (new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds(milliseconds);
#if DEBUG
            Engine.Log.Information($"获取时间" + webTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
#endif

            return webTime.ToLocalTime();
        }
        catch
        {
            DialogsManager.Confirm("网络检查不通过,请确保网络通畅再打开游戏!", btn => { Environment.Exit(0); });
            return new DateTime(0);
        }
    }

    // 小端存储与大端存储的转换
    private static uint swapEndian(ulong x)
    {
        return (uint)(((x & 0x000000ff) << 24) +
        ((x & 0x0000ff00) << 8) +
        ((x & 0x00ff0000) >> 8) +
        ((x & 0xff000000) >> 24));
    }

}
