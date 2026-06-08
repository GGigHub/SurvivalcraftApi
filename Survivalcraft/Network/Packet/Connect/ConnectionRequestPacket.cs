using System.Text;
using Engine;
using LiteNetLib;
using TemplatesDatabase;

namespace Game.Network;

public class ConnectionRequestPacket : Packet {
    public override byte ID => (byte)PacketType.ConnectRequest;
    public override NetworkState MinNeedState => NetworkState.NotConnected;

    const int verrifyMagic = 9421523;
    int m_magic;
    string m_version;
    string m_password;
    string m_user;
    string m_token;
    List<ModEntity> modInfos;
    bool m_supportsModTransfer;
    string nickname = string.Empty;
    string communityAccountId = string.Empty;

    public ConnectionRequestPacket() {
    }

    public ConnectionRequestPacket(string serverVersion, string user, string token, string passwd, List<ModEntity> modEntities) {
        if (passwd == null) passwd = string.Empty;
        if (serverVersion == null) serverVersion = string.Empty;
        m_magic = verrifyMagic;
        m_user = user;
        m_token = token;
        m_version = serverVersion;
        m_password = passwd;
        m_supportsModTransfer = true;
        modInfos = [.. modEntities];
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_magic);
        archive.Value(ref m_version);
        archive.Value(ref m_password);
        archive.Value(ref m_user);
        archive.Value(ref m_token);
        if (archive.IsReading) {
            m_supportsModTransfer = false;
            archive.TryValue(ref m_supportsModTransfer);
        }
        else {
            archive.Value(ref m_supportsModTransfer);
        }

        byte count = archive.IsWriting ? (byte)modInfos.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            modInfos = [];
            for (int i = 0; i < count; i++) {
                string resourcesMd5 = string.Empty;
                string name = string.Empty;
                string packageName = string.Empty;
                string version = string.Empty;
                archive.Value(ref resourcesMd5);
                archive.Value(ref name);
                archive.Value(ref packageName);
                archive.Value(ref version);
                modInfos.Add(new ModEntity {
                    ResourcesMd5 = resourcesMd5,
                    modInfo = new ModInfo {
                        Name = name,
                        PackageName = packageName,
                        Version = version
                    }
                });
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                var mod = modInfos[i];
                string resourcesMd5 = mod.ResourcesMd5;
                string name = mod.modInfo.Name;
                string packageName = mod.modInfo.PackageName;
                string version = mod.modInfo.Version;
                archive.Value(ref resourcesMd5);
                archive.Value(ref name);
                archive.Value(ref packageName);
                archive.Value(ref version);
            }
        }
    }

    public override void Handle(bool isServer) {
        StringBuilder connectionError = new StringBuilder();
        if (string.IsNullOrEmpty(m_token)) {
            NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket("Invalid identity token."), Request, true);
            return;
        }
        nickname = FindPlayerNameByGUID(m_token);
        AcceptAclient(m_token, connectionError);
    }

    private string FindPlayerNameByGUID(string m_token) {
        if (!Guid.TryParse(m_token, out var NetworkGuid)) {
            return "";
        }
        SubsystemGameInfo subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();
        if (subsystemGameInfo != null) {
            string entityFileDir = Storage.CombinePaths(subsystemGameInfo.DirectoryName, "PlayerEntities/");
            string entityFilePath = Storage.CombinePaths(entityFileDir, $"{NetworkGuid}.json");
            if (Storage.FileExists(entityFilePath)) {
                return LoadPlayerNameFromFile(entityFilePath, isJson: true);
            }
            entityFilePath = Storage.CombinePaths(entityFileDir, $"{NetworkGuid}.dat");
            if (Storage.FileExists(entityFilePath)) {
                return LoadPlayerNameFromFile(entityFilePath, isJson: false);
            }
        }

        return "";
    }

    private string LoadPlayerNameFromFile(string filePath, bool isJson) {
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

        return "";
    }

    public void AcceptAclient(string token, StringBuilder connectionError, bool useExternalPassword = false, string saveKey = "") {
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

        var serverMd5s = NormalizeMd5List(serverMods.Select(x => x.ResourcesMd5));
        serverMd5s.Sort(StringComparer.OrdinalIgnoreCase);

        bool modsMismatch = serverMd5s.Count != clientMd5s.Count || !serverMd5s.SequenceEqual(clientMd5s, StringComparer.OrdinalIgnoreCase);

        if (m_magic == verrifyMagic) {
            if (NetworkService.Self.NetworkGuid == guid) {
                connectionError.AppendLine("Client token matches server token.");
            }
            else if (!string.IsNullOrEmpty(pwd2)) {
                if (!useExternalPassword && m_password != pwd2) {
                    connectionError.AppendLine("Password verification failed.");
                }
            }
            else if (string.IsNullOrEmpty(token)) {
                connectionError.AppendLine("Identity verification failed.");
            }

            if (connectionError.Length > 0) {
                if (ModsManager.Configs.ContainsKey(saveKey)) {
                    ModsManager.Configs.Remove(saveKey);
                }
                NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket(connectionError.ToString()), Request, true);
                Log.Information("Received connection request from " + IPPoint + ", rejected -- " + connectionError);
            }
            else {
                if (modsMismatch) {
                    if (!m_supportsModTransfer) {
                        NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket("Mod mismatch and client does not support automatic mod transfer."), Request, true);
                        return;
                    }

                    try {
                        var acceptedLiteNetPeer = Request?.Accept();
                        if (acceptedLiteNetPeer == null) {
                            NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket("Mod mismatch and connection could not be established."), Request, true);
                            return;
                        }
                    }
                    catch (Exception e) {
                        NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket($"Mod transfer initialization failed: {e.Message}"), Request, true);
                    }
                    return;
                }

                var acceptedPeer = Request.Accept();
                ClientSession addClient = NetworkService.CreateClient((NetPeer)(object)acceptedPeer, guid, communityAccountId);
                NetworkService.Server.AddClient(addClient);
                NetworkService.Server.SendWriterFromPacket(
                    new ClientSyncListPacket(NetworkService.Server.Clients.Values) { ToPlayerIndex = addClient.PlayerIndex },
                    (NetPeer)(object)acceptedPeer,
                    true
                );

                var clientPacket = new ClientAddPacket(addClient.PlayerIndex, addClient.NetworkGuid);
                foreach (var c in NetworkService.Peers) {
                    if (c.PlayerIndex == addClient.PlayerIndex) continue;
                    if (NetworkService.Server.ClientPeers.TryGetValue(c.PlayerIndex, out var peer)) {
                        NetworkService.Server.SendWriterFromPacket(clientPacket, peer, true);
                    }
                }
                Log.Information("Received connection request from " + IPPoint + ", accepted");
            }
        }
        else {
            NetworkService.Server.SendWriterFromPacket(new ConnectionRejectPacket("Unrecognized packet header."), Request, true);
            Log.Information("Unrecognized packet header.");
        }
    }

    private static List<string> NormalizeMd5List(IEnumerable<string> md5s) {
        var sb = new StringBuilder();
        foreach (var s in md5s) {
            if (string.IsNullOrEmpty(s)) continue;
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
        if (s == null || s.Length != 32) return false;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}

