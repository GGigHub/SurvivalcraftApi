using System.Text.Json.Serialization;

namespace Game
{
    public class LoginResponse : ApiEnvelope<LoginData>
    {
    }

    public class LoginData
    {
        [JsonPropertyName("token")] public string Token { get; set; }
        [JsonPropertyName("user")] public UserData User { get; set; }
    }

    /// <summary>v4 凭据文件 payload：登录数据及其归属服务器（归一化地址，自描述）。</summary>
    public class StoredCredential
    {
        [JsonPropertyName("apiBaseUrl")] public string ApiBaseUrl { get; set; }
        [JsonPropertyName("login")] public LoginData Login { get; set; }
    }

    public class UserData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("account")] public string Account { get; set; }
        [JsonPropertyName("nickname")] public string Nickname { get; set; }
        [JsonPropertyName("lastLoginTime")] public string LastLoginTime { get; set; }
        [JsonPropertyName("exp")] public string Exp { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("accountType")] public int AccountType { get; set; }
        [JsonPropertyName("headImg")] public string HeadImg { get; set; }
        [JsonPropertyName("signature")] public string Signature { get; set; }
        [JsonPropertyName("background")] public string Background { get; set; }
        [JsonPropertyName("rank")] public object Rank { get; set; }
        [JsonPropertyName("roles")] public RoleData[] Roles { get; set; }
    }

    public class RoleData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; }
    }
}
