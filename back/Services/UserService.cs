using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ERental.Services;

public class UserService(AppDbContext db, SessionRegistry sessions)
{
    private readonly AppDbContext _db = db;
    private readonly SessionRegistry _sessions = sessions;

    ///<summary>密码长度上限：与登录列 VARCHAR(100) 保持足够余量</summary>
    private const int MaxPasswordLength = 50;

    /// <summary>
    /// 敏感账号操作前校验「当前登录管理员」的密码。
    /// 只校验一次、不签发任何令牌：密码正确只代表可以继续本次操作。
    /// </summary>
    public async Task VerifyAdminPassword(int adminId, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new BusinessException("请输入当前管理员账号的密码");
        // UserType 是 [NotMapped] 的 TPH 判别属性，不能进 LINQ；用 OfType 落到 user_type 判别列
        var admin = await _db.SystemUsers.OfType<AdminUser>().FirstOrDefaultAsync(u => u.UserId == adminId)
            ?? throw new BusinessException("当前账号不是管理员，无法执行此操作");
        if (admin.UserStatus != 1) throw new BusinessException("当前管理员账号已停用");
        if (!BCrypt.Net.BCrypt.Verify(password, admin.LoginPwd))
            throw new BusinessException("管理员密码不正确，请重试");
    }

    public async Task<List<UserDto>> GetAllStaff()    {
        return await _db.SystemUsers.Include(u => u.Role).Where(u => u.Role!.RoleName != "管理员").OrderBy(u => u.UserId).Take(Paging.MaxUnpagedRows).Select(u => new UserDto
        {
            UserId = u.UserId,
            LoginAccount = u.LoginAccount,
            UserName = u.UserName ?? "",
            RoleName = u.Role!.RoleName,
            UserStatus = u.UserStatus,
            LoginFailCount = u.LoginFailCount,
            MustChangePassword = u.MustChangePassword,
            AvatarUrl = u.AvatarUrl
        }).ToListAsync();
    }

    public async Task<UserDto> CreateStaff(CreateUserDto dto)
    {
        if (await _db.SystemUsers.AnyAsync(u => u.LoginAccount == dto.LoginAccount)) throw new BusinessException("账号已存在");
        if (!Validators.IsStrongPassword(dto.Password)) throw new BusinessException(Validators.PasswordRuleHint);
        if (dto.Password.Length > MaxPasswordLength) throw new BusinessException($"密码长度不能超过 {MaxPasswordLength} 位");
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == (dto.RoleType == "reception" ? "前台" : "财务")) ?? throw new BusinessException("角色不存在");

        //按角色落 TPH 子类：reception → ReceptionUser，finance
        var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        SystemUser user = dto.RoleType == "reception" ? SystemUser.CreateStaff<ReceptionUser>(dto.LoginAccount, hash, role.RoleId, dto.UserName) : SystemUser.CreateStaff<FinanceUser>(dto.LoginAccount, hash, role.RoleId, dto.UserName);

        _db.SystemUsers.Add(user);
        await _db.SaveChangesAsync();
        return new UserDto
        {
            UserId = user.UserId,
            LoginAccount = user.LoginAccount,
            UserName = user.UserName ?? "",
            RoleName = role.RoleName,
            UserStatus = 1,
            AvatarUrl = user.AvatarUrl
        };
    }

    public async Task UpdateStaff(int id, UpdateUserDto dto)
    {
        var user = await _db.SystemUsers.FindAsync(id) ?? throw new BusinessException("用户不存在");

        //停用（或删除）前的最后一名管理员保护：见 EnsureNotLastActiveAdmin 注释
        if (dto.UserStatus == 0 && user.UserStatus == 1) await EnsureNotLastActiveAdmin(user, "停用");

        user.Rename(dto.UserName);
        //密码不再由「编辑资料」直接写入：重置必须走一次性临时密码接口，
        //否则管理员改完密码既无法保证首次登录改密，也没有可交付的临时凭据。
        if (!string.IsNullOrEmpty(dto.Password))
            throw new BusinessException("请使用「重置密码」生成一次性临时密码，不要直接修改密码");
        if (dto.UserStatus.HasValue) user.ChangeStatus(dto.UserStatus.Value);
        await _db.SaveChangesAsync();

        //停用账号 → 立刻清掉该账号的在线会话
        if (dto.UserStatus == 0) await _sessions.RevokeAllAsync(user.UserType, id, "账号已被管理员停用");
    }

    ///<summary>管理员生成一次性临时密码：本人首次登录必须改密，24 小时后失效</summary>
    public async Task<ResetPasswordResultDto> ResetPasswordAsync(int id)
    {
        var user = await _db.SystemUsers.FindAsync(id) ?? throw new BusinessException("用户不存在");
        if (user.UserStatus == 0) throw new BusinessException("账号已停用，请先启用再重置密码");

        var temp = GenerateTemporaryPassword();
        var expires = AppClock.Now.AddHours(24);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(temp), requireChange: true, expire: expires);
        await _db.SaveChangesAsync();
        await _sessions.RevokeAllAsync(user.UserType, id, "管理员已重置密码，请用一次性临时密码重新登录");

        return new ResetPasswordResultDto { TemporaryPassword = temp, ExpiresAt = expires };
    }

    ///<summary>生成至少 8 位、同时含字母与数字的临时密码（去掉易混淆字符）</summary>
    private static string GenerateTemporaryPassword()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        Span<char> chars = stackalloc char[10];
        for (var i = 0; i < chars.Length; i++)
        {
            var source = i % 2 == 0 ? letters : digits;
            chars[i] = source[RandomNumberGenerator.GetInt32(source.Length)];
        }
        //保证两种字符都出现（上面的交替生成已经满足）；再打乱顺序，避免固定模式
        var shuffled = chars.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return new string(shuffled);
    }

    public async Task DeleteStaff(int id)
    {
        var user = await _db.SystemUsers.FindAsync(id) ?? throw new BusinessException("用户不存在");
        if (user.LoginAccount == "trq") throw new BusinessException("默认管理员不可删除");
        await EnsureNotLastActiveAdmin(user, "删除");
        //日志只增不删：账号可以删，历史操作不能跟着消失
        await _db.SystemLogs.Where(l => l.UserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UserId, (int?)null));
        _db.SystemUsers.Remove(user);
        await _db.SaveChangesAsync();
        await _sessions.RevokeAllAsync(user.UserType, id, "账号已被删除，登录状态已失效");
    }

    ///<summary>最后一名管理员保护</summary>
    private async Task EnsureNotLastActiveAdmin(SystemUser user, string action)
    {
        var roleName = await _db.Roles.Where(r => r.RoleId == user.RoleId).Select(r => r.RoleName).FirstOrDefaultAsync();
        if (roleName != "管理员") return;

        //已被停用的管理员不占名额
        if (user.UserStatus != 1) return;

        var activeAdmins = await _db.SystemUsers.CountAsync(u => u.Role!.RoleName == "管理员" && u.UserStatus == 1);
        if (activeAdmins <= 1) throw new BusinessException($"系统中至少保留一名启用状态的管理员，无法{action}「{user.LoginAccount}」");
    }
}
