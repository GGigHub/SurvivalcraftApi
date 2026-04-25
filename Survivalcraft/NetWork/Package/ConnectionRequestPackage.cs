using System.Text;
using Engine;
using LiteNetLib;
using TemplatesDatabase;

namespace Game.NetWork.Packages;

public class ConnectionRequestPackage : IPackage {
    public byte ID => (byte)PackageType.ConnectRequest;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.NotConnected;
    const int verrifyMagic = 9421523;//校验码
    int m_magic;
    string m_version;
    string m_password;
    string m_user;
    string m_token;
    Guid m_tmp_token;
    List<ModEntity> modInfos;

    bool m_supportsModTransfer;

    string nickname = string.Empty;
    string communityAccountId = string.Empty;

    public ConnectionRequestPackage() {

    }

    public ConnectionRequestPackage(Guid tmp_token, string serverVersion, string user, string token, string passwd, List<ModEntity> modEntities) {
        if (passwd == null) passwd = string.Empty;
        if (serverVersion == null) serverVersion = string.Empty;
        m_magic = verrifyMagic;
        m_tmp_token = tmp_token;
        m_user = user;
        m_token = token;
        m_version = serverVersion;
        m_password = passwd;
        m_supportsModTransfer = true;
        modInfos = [.. modEntities];
    }

    public void Handle(bool isServer) {
        StringBuilder ConnectionError = new StringBuilder();
        if (string.IsNullOrEmpty(m_token)) {
            NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage("身份信息为空，验证失败"), From.Request, true);
            return;
        }
        nickname = FindPlayerNameByGUID(m_token);
        AcceptAclient(m_token, ConnectionError);
    }

    private string FindPlayerNameByGUID(string m_token) {
        try {
            if (!Guid.TryParse(m_token, out var playerGuid)) {
                return "";
            }
            SubsystemGameInfo subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();
            if (subsystemGameInfo != null) {
                string entityFileDir = Storage.CombinePaths(subsystemGameInfo.DirectoryName, "PlayerEntities/");
                string entityFilePath = Storage.CombinePaths(entityFileDir, $"{playerGuid}.json");
                if (Storage.FileExists(entityFilePath)) {
                    return LoadPlayerNameFromFile(entityFilePath, isJson: true);
                }
                entityFilePath = Storage.CombinePaths(entityFileDir, $"{playerGuid}.dat");
                if (Storage.FileExists(entityFilePath)) {
                    return LoadPlayerNameFromFile(entityFilePath, isJson: false);
                }
            }
        }
        catch (Exception ex) {
            Log.Warning("查找玩家名称时发生错误: " + ex.Message);
        }
        return "";
    }

    private string LoadPlayerNameFromFile(string filePath, bool isJson) {
        try {
            using (Stream stream = Storage.OpenFile(filePath, OpenFileMode.Read)) {
                ValuesDictionary dict = new ValuesDictionary();
                if (isJson) {
                    using (StreamReader reader = new StreamReader(stream)) {
                        string jsonText = reader.ReadToEnd();
                        dict.ApplyOverridesUseJson(jsonText, out var _);
                    }
                }
                else {
                    byte[] data2 = new byte[stream.Length];
                    stream.Read(data2, 0, data2.Length);
                    dict.ApplyOverridesUseMessagePack(data2);
                }
                ValuesDictionary playerDataDict = dict.GetValue<ValuesDictionary>("Data");
                if (playerDataDict != null) {
                    string playerName = playerDataDict.GetValue("Name", "");
                    if (!string.IsNullOrEmpty(playerName)) {
                        return playerName;
                    }
                }
            }
        }
        catch (Exception ex) {
            Log.Warning("从文件 " + filePath + " 加载玩家名称时发生错误: " + ex.Message);
        }
        return "";
    }

    public void AcceptAclient(string token, StringBuilder ConnectionError, bool useExternalPassword = false, string saveKey = "") {
        Guid guid = Guid.Empty;
        if (!string.IsNullOrEmpty(token)) {
            guid = new Guid(token);
        }
        var pwd2 = GameManager.Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.Password;
        var mList = ModsManager.ModList;
        var serverMods = mList.Where(m => m.modInfo.PackageName != "survivalcraft" && m.modInfo.PackageName != "com.fastdebug").ToList();
        var clientMods = modInfos.Where(m => m.modInfo.PackageName != "survivalcraft" && m.modInfo.PackageName != "com.fastdebug").ToList();

        var clientMd5s = NormalizeMd5List(clientMods.Select(x => x.ResourcesMd5));
        clientMd5s.Sort(StringComparer.OrdinalIgnoreCase);

        var serverMd5Source = serverMods.Select(x => x.ResourcesMd5);

        var serverMd5s = NormalizeMd5List(serverMd5Source);
        serverMd5s.Sort(StringComparer.OrdinalIgnoreCase);

        bool modsMismatch = serverMd5s.Count != clientMd5s.Count || !serverMd5s.SequenceEqual(clientMd5s, StringComparer.OrdinalIgnoreCase);

        if (m_magic == verrifyMagic) {
            if (NetworkManager.Self.PlayerGuid == guid) {
                ConnectionError.AppendLine("客户端和服务器 token 相同");
            }
            else if (NetworkManager.Peers.FirstOrDefault(c => c != From && (c.PlayerGuid == guid || c.TokenId == m_tmp_token)) != null) {
                ConnectionError.AppendLine("你的 ID 与服务器中某个在线玩家的 ID 相同");
            }
            else if (!string.IsNullOrEmpty(pwd2)) {
                if (!useExternalPassword && m_password != pwd2) {
                    ConnectionError.AppendLine("房间密码验证错误");
                }
            }
            else if (string.IsNullOrEmpty(token)) {
                ConnectionError.AppendLine("身份信息验证错误");
            }
            if (ConnectionError.Length > 0) {
                if (ModsManager.Configs.ContainsKey(saveKey)) // 如果验证失败，应该清理登录信息
                {
                    ModsManager.Configs.Remove(saveKey);
                }
                NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage(ConnectionError.ToString()), From.Request, true);
                Log.Information("Received connection request from " + From.IPPoint + ", rejected -- " + ConnectionError.ToString());
            }
            else {
                if (modsMismatch) {
                    if (!m_supportsModTransfer) {
                        NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage("模组不一致，且客户端不支持自动下载模组，请更新客户端或手动安装模组后再加入"), From?.Request, true);
                        return;
                    }

                    // Accept connection but do not proceed with normal join.
                    // We'll push server mod manifest and stream required .netmod files, then client reboots.
                    try {
                        static string GetFileName(string path) {
                            if (string.IsNullOrEmpty(path))
                                return string.Empty;
                            int slash = path.LastIndexOf('/');
                            if (slash >= 0 && slash < path.Length - 1)
                                return path.Substring(slash + 1);
                            return path;
                        }

                        var acceptedLiteNetPeer = From?.Request?.Accept();
                        if (acceptedLiteNetPeer == null) {
                            NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage("模组不一致且无法建立连接"), From?.Request, true);
                            return;
                        }

                    }
                    catch (Exception e) {
                        NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage($"模组传输初始化失败: {e.Message}"), From?.Request, true);
                    }
                    return;
                }

                var acceptedPeer = From.Request.Accept();
                NetworkManager.PendingPeer = (NetPeer)(object)acceptedPeer;
                Client AddClient = NetworkManager.CreateClient(NetworkManager.PendingPeer, m_tmp_token, guid, communityAccountId, nickname);
                var clientPackage = new ClientPackage(AddClient.ID, AddClient.TokenId, AddClient.PlayerGuid, AddClient.Nickname);
                foreach (var c in NetworkManager.Peers) {
#if DEBUG
                    Log.Information($"通知 Client[{c.ID}] 新加入客户端 {AddClient.ID}");
#endif
                    NetworkManager.AgreeOnPendingPeer.Add(c.ID);
                    NetworkManager.Server.SendWriterFromPackage(clientPackage, c.Peer, true);
                }
                Log.Information("Received connection request from " + From.IPPoint + ", accepted");
                NetworkManager.Server.DeliveryEvent(null, null);
            }
        }
        else {
            NetworkManager.Server.SendWriterFromPackage(new ConnectionRejectPackage("无法识别的数据包头"), From.Request, true);
            Log.Information("无法识别的数据包头");
        }

    }

    public void ReadData(PackageStreamReader reader) {
        m_magic = reader.ReadInt32();
        m_version = reader.ReadString();
        m_password = reader.ReadString();
        m_user = reader.ReadString();
        m_tmp_token = reader.ReadGuid();
        m_token = reader.ReadString();
        // Capability flag was added later; keep backward compatibility.
        m_supportsModTransfer = false;
        try {
            m_supportsModTransfer = reader.ReadBoolean();
        }
        catch {
            m_supportsModTransfer = false;
        }
        byte count = reader.ReadByte();
        modInfos = new List<ModEntity>();
        for (int i = 0; i < count; i++) {
            ModEntity modEntity = new ModEntity();
            modEntity.ResourcesMd5 = reader.ReadString();
            modEntity.modInfo = new ModInfo();
            modEntity.modInfo.Name = reader.ReadString();
            modEntity.modInfo.PackageName = reader.ReadString();
            modEntity.modInfo.Version = reader.ReadString();
            modInfos.Add(modEntity);
        }
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(m_magic);
        writer.Write(m_version);
        writer.Write(m_password);
        writer.Write(m_user);
        writer.Write(m_tmp_token);
        writer.Write(m_token);
        writer.Write(m_supportsModTransfer);
        writer.Write((byte)modInfos.Count);
        foreach (var m in modInfos) {
            writer.Write(m.ResourcesMd5);
            writer.Write(m.modInfo.Name);
            writer.Write(m.modInfo.PackageName);
            writer.Write(m.modInfo.Version);
        }
    }

    private static List<string> NormalizeMd5List(IEnumerable<string> md5s) {
        var sb = new StringBuilder();
        foreach (var s in md5s) {
            if (string.IsNullOrEmpty(s))
                continue;
            sb.Append(s.Trim());
        }
        var concat = sb.ToString();
        var result = new List<string>();
        for (int i = 0; i + 32 <= concat.Length; i += 32) {
            var chunk = concat.Substring(i, 32).ToLowerInvariant();
            if (IsHex32(chunk)) {
                result.Add(chunk);
            }
        }
        return result;
    }

    private static bool IsHex32(string s) {
        if (s == null || s.Length != 32)
            return false;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

}
