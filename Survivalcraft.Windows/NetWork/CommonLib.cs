using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Game;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib.Utils;
using TemplatesDatabase;

namespace Game.NetWork;

public static class CommonLib
{
    public const int DISCONNECT_TIMEOUT = 10000;

    public static NetNode Net = new();

    public static ModTransferState ModTransferState = new();

    public static Texture2D BlockTexture;

    public static bool IsOfflineMode;
    public static WorkType WorkType => Net.WorkType;

    public static bool IsWorkTypeValid()
    {
        return WorkType == GetCurrentWorkType();
    }

    public static WorkType GetCurrentWorkType()
    {
        if (Net.IsServer) return WorkType.Server;
        if (Net.CurrentStage == NetNode.Stage.Connected) return WorkType.Client;
        return WorkType.Local;
    }

    /// <summary>
    /// 获取玩家的有效模式：当玩家设置为 Follow 时，返回世界模式；否则返回玩家自身设置的模式。
    /// </summary>
    public static GameMode GetEffectiveGameMode(PlayerData pd)
    {
        if (pd == null) return ProjectNet.subsystemGameInfo?.WorldSettings?.GameMode ?? GameMode.Survival;
        return pd.PlayerGameMode == GameMode.Follow
            ? (ProjectNet.subsystemGameInfo?.WorldSettings?.GameMode ?? GameMode.Survival)
            : pd.PlayerGameMode;
    }

    public static ComponentPlayer MainPlayer
    {
        get
        {
            if (GameManager.Project != null && Net.Self != null)
            {
                return GameManager.Project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerGUID == Net.Self.PlayerGuid);
            }
            return null;
        }
    }
    public static bool StartServer()
    {
        return Net.StartServer(SettingsManager.ServerPort);
    }

    public static NetDataWriter GetWriter(PackageStreamWriter writer, out int size)
    {
        byte[] data = writer.Data();
        NetDataWriter tmp = new NetDataWriter();
        tmp.PutBytesWithLength(data);
        size = data.Length;
        return tmp;
    }

    public static PackageStreamReader GetReader(NetDataReader reader)
    {
        return new PackageStreamReader(reader.GetBytesWithLength());
    }

    /// <summary>
    /// 写入服务器信息
    /// </summary>
    /// <param name="w"></param>
    public static void WriteServerInfo(NetDataWriter w)
    {
        w.Put(ProjectNet.Server_Version);
        w.Put((ushort)(Net.ClientCount));
        w.Put(ProjectNet.subsystemGameInfo.WorldSettings.MaxOnlinePlayerCount);
        w.Put((byte)ProjectNet.subsystemGameInfo.WorldSettings.GameMode);




        w.Put(false);
        w.Put(ProjectNet.subsystemTimeOfDay.CalculateTimeOfDay());
    }

    public static void ReadServerInfo(NetPlayScreen.Connect c, NetDataReader r, System.Diagnostics.Stopwatch s, IPEndPoint ip, bool isLocal = false)
    {
        c.version = r.GetString();
        c.playerCount = r.GetUShort();
        c.maxCount = r.GetUShort();
        c.gameMode = (GameMode)r.GetByte();
        c.HasPassword = r.GetBool();
        r.GetBool();
        c.TimeOfDay = r.GetFloat();
        c.state = NetPlayScreen.ConnectState.Avaliable;
        c.usedTime = s.ElapsedMilliseconds;
        if (isLocal)
        {
            c.name = "[本地] " + c.name;
            c.ip = ip.Address.ToString() + ":" + ip.Port;
        }
    }


    public static string GetInnerIp()
    {
        var ipHost = Dns.GetHostAddresses(Dns.GetHostName());
        foreach (var ip in ipHost)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return ipHost[0].ToString();
    }

    public static bool Resolve(string ip, out IPEndPoint ep)
    {
        if (Uri.TryCreate("http://" + ip, UriKind.Absolute, out Uri uri))
        {
            int port = uri.IsDefaultPort ? SettingsManager.ServerPort : uri.Port;
            if (IPAddress.TryParse(uri.Host, out IPAddress addr))
            {
                ep = new IPEndPoint(addr, port);
                return true;
            }
            var entry = Dns.GetHostEntry(uri.Host);
            if (entry.AddressList.Length > 0)
            {
                ep = new IPEndPoint(entry.AddressList[0], port);
                return true;
            }
        }
        ep = null;
        return false;
    }

    public static byte[] GetNowProject(Project project, Client client)
    {
        if (project is not ProjectNet net) return null;

        net.SendToClientMode = true;
        try
        {
            ValuesDictionary rootNode = CreateProjectRootNode(project);
            OverrideGameModeForClient(rootNode, client);
            ReplacePlayerGUIDsWithTokenIds(rootNode, client);
            return rootNode.ToMessagePack();
        }
        finally
        {
            net.SendToClientMode = false;
        }
    }

    private static ValuesDictionary CreateProjectRootNode(Project project)
    {
        var projData = Save(project);

        ValuesDictionary rootNode = [];
        projData.Save(rootNode);
        return rootNode;
    }

    private static ProjectData Save(Project project)
    {
        ProjectData projectData = new ProjectData();
        projectData.ValuesDictionary = new ValuesDictionary();
        projectData.ValuesDictionary.DatabaseObject = project.m_projectTemplate;
        foreach (GameEntitySystem.Subsystem subsystem in GameManager.Project.m_subsystems)
        {
            ValuesDictionary valuesDictionary = new ValuesDictionary();
            subsystem.SaveForClient(valuesDictionary);
            if (valuesDictionary.Count > 0)
            {
                projectData.ValuesDictionary.SetValue(subsystem.ValuesDictionary.DatabaseObject.Name, valuesDictionary);
            }
        }
        return projectData;
    }

    private static void OverrideGameModeForClient(ValuesDictionary rootNode, Client client)
    {
        try
        {
            PlayerData playerData = FindPlayerDataForClient(client);
            if (playerData == null || playerData?.PlayerGameMode == GameMode.Follow) return;

            var gameInfo = rootNode?
                .GetValue("Subsystems", new ValuesDictionary())
                .GetValue("GameInfo", null as ValuesDictionary);

            gameInfo?.SetValue("GameMode", playerData?.PlayerGameMode);
        }
        catch (Exception e)
        {
            Log.Information($"OverrideGameModeForClient error: {e.Message}");
        }
    }

    private static PlayerData FindPlayerDataForClient(Client client)
    {
        if (client.PlayerData != null)
            return client.PlayerData;

        return ProjectNet.subsystemPlayers?
            .PlayersData
            .FirstOrDefault(pd => pd.PlayerGUID == client.PlayerGuid);
    }

    private static void ReplacePlayerGUIDsWithTokenIds(ValuesDictionary rootNode, Client client)
    {
        try
        {
            var playersSubsystem = rootNode?
                .GetValue("Subsystems", new ValuesDictionary())
                .GetValue("Players", null as ValuesDictionary);

            if (playersSubsystem == null) return;

            var playersList = playersSubsystem.GetValue("Players", null as ValuesDictionary);

            if (playersList != null)
            {
                foreach (var playerVD in playersList.Values.OfType<ValuesDictionary>())
                {
                    Guid originalGuid = playerVD.GetValue("PlayerGUID", Guid.Empty);
                    if (originalGuid != Guid.Empty)
                    {
                        ReplaceGUIDIfNeeded(playerVD, client, originalGuid);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Information($"ReplacePlayerGUIDsWithTokenIds error: {e.Message}");
        }
    }

    private static void ReplaceGUIDIfNeeded(ValuesDictionary playerVD, Client currentClient, Guid originalGuid)
    {
        var targetClient = Net.GetClientByGUID(originalGuid);

        if (targetClient != null && targetClient.PlayerGuid != currentClient.PlayerGuid)
        {
            playerVD.SetValue("PlayerGUID", targetClient.TokenId);
        }
    }

    public static byte[] Compress(Stream stream)
    {
        stream.Position = 0L;
        byte[] data = new byte[stream.Length];
        stream.Read(data, 0, (int)stream.Length);
        return Compress(data);
    }

    public static byte[] Compress(byte[] datas)
    {
        byte[] data = null;
        using (var outStream = new MemoryStream())
        {
            using (var zipStream = new DeflateStream(outStream, CompressionMode.Compress))
            {
                zipStream.Write(datas, 0, datas.Length);
            }
            data = outStream.ToArray();
        }
        return data;
    }

    public static byte[] Decompress(byte[] inputBytes)
    {
        var outStream = new MemoryStream();
        using (var inputStream = new MemoryStream(inputBytes))
        {
            using (var zipStream = new DeflateStream(inputStream, CompressionMode.Decompress))
            {
                zipStream.CopyTo(outStream);
                zipStream.Close();
            }
        }
        return outStream.ToArray();
    }

    public static string SerializeVDict(ValuesDictionary dict)
    {
        var elem = new XElement("Values");
        dict.Save(elem);
        return XmlUtilities.XmlUtils.SaveXmlToString(elem, true);
    }

    public static ValuesDictionary ReadVDict(string str)
    {
        var dict = new ValuesDictionary();
        var elem = XmlUtilities.XmlUtils.LoadXmlFromString(str, true);
        dict.ApplyOverrides(elem);
        return dict;
    }


    /// <summary>
    /// 获取时间戳，单位0.1ms
    /// </summary>
    /// <returns></returns>
    public static long GetMicroSeconds() => DateTime.Now.Ticks / 10000;

#if CHUNK_TIMER
    static System.Diagnostics.Stopwatch chunkStopwatch = new System.Diagnostics.Stopwatch();
#endif
}
