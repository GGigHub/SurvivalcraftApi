using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Engine;

public class NetworkLogSink : ILogSink, IDisposable {
    private UdpClient _udpClient;
    private IPEndPoint _remoteEndPoint;

    public NetworkLogSink(string ipAddress = "127.0.0.1", int port = 23942) {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--log-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) {
                port = p;
            }
        }
        _udpClient = new UdpClient();
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
    }

    public void Log(LogType type, string message) {
        try {
            string logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}\n";
            if (type == LogType.Error) logEntry += new StackTrace().ToString();
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