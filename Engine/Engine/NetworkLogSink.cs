using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Engine;

public class NetworkLogSink : ILogSink, IDisposable {
    private UdpClient _udpClient;
    private IPEndPoint _remoteEndPoint;

    public NetworkLogSink(string ipAddress = "127.0.0.1", int port = 23942) {
        _udpClient = new UdpClient();
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
    }

    public void Log(LogType type, string message) {
        try {
            string logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}\n";
            byte[] bytes = Encoding.UTF8.GetBytes(logEntry);

            _udpClient.SendAsync(bytes, bytes.Length, _remoteEndPoint);
        }
        catch {
        }
    }

    public void Dispose() {
        _udpClient?.Dispose();
    }
}