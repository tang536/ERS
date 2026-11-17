using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ERental.Data;
using ERental.Services;

namespace ERental.Models;

///<summary>
/// 会话登记表：从 system_config 独立出来的单点登录状态。
/// 一行对应一个「账号类型 + 用户 ID」，保存当前会话 sid、续期 jti 和强制下线原因。
/// 独立成表后，登录/强退不再与支付配置等业务键竞争同一张配置表。
///</summary>
[Table("session_state")]
public class SessionState
{
    private const int MaxSessionKey = 64;
    private const int MaxToken = 64;
    private const int MaxRevokedReason = 100;

    [Key]
    [Column("session_id")]
    public int SessionId { get; init; }

    ///<summary>账号唯一键：staff.1 / customer.1（带类型前缀，员工与顾客 ID 空间分开）</summary>
    [Required, MaxLength(MaxSessionKey)]
    [Column("session_key")]
    public string SessionKey { get; init; } = string.Empty;

    ///<summary>当前会话 sid；空串表示该账号已被强制下线，等待中间件给出原因</summary>
    [MaxLength(MaxToken)]
    [Column("session_token")]
    public string? SessionToken { get; private set; }

    ///<summary>续期令牌 jti：轮换后旧 jti 立即失效</summary>
    [MaxLength(MaxToken)]
    [Column("refresh_jti")]
    public string? RefreshJti { get; private set; }

    ///<summary>强制下线原因；普通主动退出不写墓碑</summary>
    [MaxLength(MaxRevokedReason)]
    [Column("revoked_reason")]
    public string? RevokedReason { get; private set; }

    [Column("update_time")]
    public DateTime UpdateTime { get; private set; } = AppClock.Now;

    public static SessionState Create(string key, string? token, string? refreshJti, string? revokedReason)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxSessionKey)
            throw new BusinessException("会话键无效");
        return new SessionState
        {
            SessionKey = key.Trim(),
            SessionToken = Clip(token, MaxToken),
            RefreshJti = Clip(refreshJti, MaxToken),
            RevokedReason = Clip(revokedReason, MaxRevokedReason),
            UpdateTime = AppClock.Now,
        };
    }

    ///<summary>覆盖会话内容：登录、续期轮换、强制下线都走这里</summary>
    public void Update(string? token, string? refreshJti, string? revokedReason)
    {
        SessionToken = Clip(token, MaxToken);
        RefreshJti = Clip(refreshJti, MaxToken);
        RevokedReason = Clip(revokedReason, MaxRevokedReason);
        UpdateTime = AppClock.Now;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
