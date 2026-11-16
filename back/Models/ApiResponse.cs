using System.Text.Json.Serialization;

namespace ERental.Models;

public class ApiResponse<T>
{
    public int Code { get; init; } = 200;
    public string Message { get; init; } = "success";
    public bool Success { get; init; } = true;
    public T? Data { get; init; }

    /// <summary>
    /// 请求追踪号：仅错误响应携带，成功响应不输出（<see cref="JsonIgnoreCondition.WhenWritingNull"/>）。
    /// 用户报「接口 500」时把这个值发过来，就能在服务端日志里精确定位到同一次请求。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }
}
