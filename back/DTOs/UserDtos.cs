namespace ERental.DTOs;

public class CreateUserDto
{
    public string LoginAccount { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string RoleType { get; init; } = string.Empty; // reception / finance
}

public class UpdateUserDto
{
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public int? UserStatus { get; init; }
}

public class UserDto
{
    public int UserId { get; init; }
    public string LoginAccount { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public int UserStatus { get; init; }
    public int LoginFailCount { get; init; }
    /// <summary>是否存在待本人修改的临时密码</summary>
    public bool MustChangePassword { get; init; }
}

/// <summary>管理员重置密码的一次性结果：明文只在这一次响应里出现，前端展示后不落库</summary>
public class ResetPasswordResultDto
{
    public string TemporaryPassword { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

