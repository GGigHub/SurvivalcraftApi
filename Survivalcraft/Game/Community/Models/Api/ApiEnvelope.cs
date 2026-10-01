using System.Text.Json.Serialization;

namespace Game {
    /// <summary>
    /// 社区 API 的 {code, data, msg} 标准信封。各响应类型以空壳继承复用这三个属性，
    /// 反序列化/访问方式与手写副本完全一致。
    /// </summary>
    public class ApiEnvelope<T> {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("data")] public T Data { get; set; }
        [JsonPropertyName("msg")] public string Msg { get; set; }
    }
}