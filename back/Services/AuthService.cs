using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ERental.Authorization;
using ERental.Data;
using ERental.DTOs;
using ERental.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ERental.Services;

public class AuthService(AppDbContext db, IConfiguration config, SessionRegistry sessions, LoginIpGuard ipGuard, IHttpContextAccessor http)
{
    ///<summary>用户类型：顾客</summary>
    private const string UserTypeCustomer = "customer";

    ///<summary>账号不存在时的统一提示</summary>
    private const string MsgAccountNotFound = "账号不存在";

    private readonly AppDbContext _db = db;
    private readonly IConfiguration _config = config;
    private readonly SessionRegistry _sessions = sessions;
    private readonly LoginIpGuard _ipGuard = ipGuard;
    private readonly IHttpContextAccessor _http = http;

    //角色名常量：与 DTOs/AuthDtos 的 RoleType（admin/reception/finance）、数据库 Roles.RoleName 种子值、[Authorize(Roles="...")] 三处保持同源
    private const string RoleAdmin = "管理员";
    private const string RoleReception = "前台";
    private const string RoleFinance = "财务";
    public const string RoleCustomer = "顾客";

    ///<summary>专供「账号不存在」时消耗等量算力的假哈希</summary>
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), workFactor: 11);

    ///<summary>访问令牌类型：有效期短，用于每次业务请求（仅本服务内部使用，外部只判断"是不是续期令牌"）</summary>
    private const string TokenTypeAccess = "access";

    ///<summary>续期令牌类型：有效期长，只用于换新令牌，不能直接访问业务接口</summary>
    public const string TokenTypeRefresh = "refresh";

    ///<summary>JWT 中区分令牌用途的声明名</summary>
    public const string TokenTypeClaim = "token_type";

    ///<summary>员工登录</summary>
    public async Task<LoginResponseDto> StaffLogin(StaffLoginDto dto, string? clientIp = null)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp;

        //阈值读系统配置：登录是低频操作，一次 ToList 的代价可忽略
        var (maxFail, lockMinutes) = await LoadLoginGuardAsync();

        //① IP 封锁闸：先于账号存在性判断
        if (_ipGuard.IsLocked(ip, out var lockTo)) throw new BusinessException($"当前网络登录失败次数过多，已被暂时封锁，请于 {lockTo:HH:mm} 后重试");

        //MySQL 列排序规则为 _ci（不区分大小写），SQL 比较会让 TRQ 命中 trq
        var candidates = await _db.SystemUsers.Include(u => u.Role).Where(u => u.LoginAccount == dto.LoginAccount).ToListAsync();
        var user = candidates.FirstOrDefault(u => string.Equals(u.LoginAccount, dto.LoginAccount, StringComparison.Ordinal));
        if (user == null)
        {
            //时序侧信道：账号存在时这里会跑一次 BCrypt（数十至上百毫秒），账号不存在则几乎立刻返回
            _ = BCrypt.Net.BCrypt.Verify(dto.Password, DummyPasswordHash);

            //账号不存在也要记 IP 一笔
            var notFoundLocked = _ipGuard.RegisterFailure(ip, maxFail, lockMinutes, out _);
            //登录、异常行为全流程留痕（日志不可删除、可追溯）
            await WriteLog(null, $"登录失败：账号不存在（{dto.LoginAccount}，IP {ip}）", "用户登录", "失败");
            //对外文案必须与「密码错误」逐字一致
            if (notFoundLocked) throw new BusinessException($"账号或密码错误，当前网络已被封锁{lockMinutes}分钟，请稍后再试");
            throw new BusinessException($"账号或密码错误，当前网络还剩 {_ipGuard.Remaining(ip, maxFail)} 次机会");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.LoginPwd))
        {
            //账号维度只累加失败次数（员工管理页「失败次数」列留痕用），不再触发任何锁定
            user.RecordLoginFailure();
            await _db.SaveChangesAsync();

            //网络维度：密码错误记 IP 一笔，达到阈值封锁该 IP
            var locked = _ipGuard.RegisterFailure(ip, maxFail, lockMinutes, out _);
            if (locked)
            {
                await WriteLog(user.UserId, $"登录失败次数过多，IP {ip} 已被封锁{lockMinutes}分钟（账号 {user.LoginAccount}）", "用户登录", "失败");
                throw new BusinessException($"账号或密码错误，当前网络已被封锁{lockMinutes}分钟，请稍后再试");
            }
            var left = _ipGuard.Remaining(ip, maxFail);
            await WriteLog(user.UserId,
                $"登录失败：密码错误，当前网络还剩 {left} 次机会（{user.LoginAccount}，IP {ip}）", "用户登录", "失败");
            //与「账号不存在」分支同一句话
            throw new BusinessException($"账号或密码错误，当前网络还剩 {left} 次机会");
        }

        //── 以下三项检查全部放在密码校验之后 ──原因：这三条都不是「密码对不对」的问题

        if (user.UserStatus == 0)
        {
            await WriteLog(user.UserId, $"登录失败：账号已被禁用（{user.LoginAccount}）", "用户登录", "失败");
            throw new BusinessException("账号已被禁用，请联系管理员");
        }
        //账号侧 lock_until 不再由登录逻辑写入（自动锁账号=可被利用的拒绝服务，见方法注释）
        if (user.LockUntil.HasValue && user.LockUntil > AppClock.Now) throw new BusinessException($"账号已锁定，请于 {user.LockUntil:HH:mm} 后重试");
        if (user.MustChangePassword && user.PwdResetExpire.HasValue && user.PwdResetExpire < AppClock.Now)
            throw new BusinessException("临时密码已过期，请联系管理员重新生成");

        //角色分端隔离：登录页选的是「管理员/前台/财务」哪个页签，就必须是该角色的账号
        var actualRole = user.Role?.RoleName ?? RoleReception;
        var expectedRole = MapRoleType(dto.RoleType);
        if (expectedRole != null && actualRole != expectedRole)
        {
            await WriteLog(user.UserId, $"登录失败：角色不匹配（账号 {user.LoginAccount} 实际为「{actualRole}」，却在「{expectedRole}」入口登录）", "用户登录", "失败");
            throw new BusinessException($"该账号是「{actualRole}」账号，请在对应的登录入口登录");
        }

        //登录成功：清零账号失败计数，并放掉该 IP 的失败记录
        user.RecordLoginSuccess();
        await _db.SaveChangesAsync();
        _ipGuard.Reset(ip);
        await WriteLog(user.UserId, $"登录成功（{user.LoginAccount}，IP {ip}）", "用户登录", "成功");

        //关键：角色一律以数据库中的真实角色为准，前端不再根据"登录时点的哪个页签"决定跳转
        var roleName = user.Role?.RoleName ?? RoleReception;
        //单点登录 + 刷新令牌轮换：刷新令牌带独立 jti，登录时与 sid 一起登记
        var refreshJti = Guid.NewGuid().ToString("N");
        var sid = await _sessions.IssueAsync(user.UserType, user.UserId, refreshJti);
        var token = GenerateJwt(user.UserId, user.LoginAccount, user.UserType, roleName, sid, mustChangePassword: user.MustChangePassword);
        return new LoginResponseDto
        {
            Token = token,
            //续期令牌
            RefreshToken = GenerateJwt(user.UserId, user.LoginAccount, user.UserType, roleName, sid, TokenTypeRefresh, RefreshLifetime(), refreshJti, user.MustChangePassword),
            UserType = user.UserType,
            RoleName = roleName,
            //权限项：代码层授予清单（RolePermissionMap）对接出的权限编码
            PermissionCodes = await LoadPermissionCodesAsync(user.Role),
            UserName = user.UserName ?? user.LoginAccount,
            UserId = user.UserId,
            ProfileCompleted = true,
            MustChangePassword = user.MustChangePassword,
            PasswordResetExpire = user.PwdResetExpire
        };
    }

    ///<summary>登录防护阈值：员工与顾客共用同一组系统配置</summary>
    private async Task<(int MaxFail, int LockMinutes)> LoadLoginGuardAsync()
    {
        var configs = await _db.SystemConfigs.ToListAsync();
        return (ConfigValue.Int(configs, "max_login_fail", 5), ConfigValue.Int(configs, "lock_minutes", 10));
    }

    ///<summary>登录页页签（admin/reception/finance）→ 系统角色名 的映射</summary>
    private static string? MapRoleType(string? roleType) => roleType?.Trim().ToLowerInvariant() switch
    {
        "admin" => RoleAdmin,
        "reception" => RoleReception,
        "finance" => RoleFinance,
        _ => null
    };

    ///<summary>取角色被授予的权限编码</summary>
    private async Task<IReadOnlyList<string>> LoadPermissionCodesAsync(Role? role)
    {
        if (role is null) return [];
        var permissions = await _db.Permissions.AsNoTracking().ToListAsync();
        RolePermissionMap.Link(role, permissions);
        return role.Permissions
            .Select(p => p.PermissionCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .ToList();
    }

    ///<summary>顾客登录：只认「用户名+密码」</summary>
    public async Task<LoginResponseDto> CustomerLogin(CustomerLoginDto dto, string? clientIp = null)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp;
        if (string.IsNullOrWhiteSpace(dto.Account)) throw new BusinessException("请输入用户名");

        var account = dto.Account.Trim();

        //顾客端与员工端共用登录防护闸，但键按「账号 + IP」组合：
        //只在同一账号反复试错时封锁，避免校园/网吧共用出口 IP 时一个账号把整网都锁死
        var guardKey = $"customer|{account.ToLowerInvariant()}|{ip}";
        var (maxFail, lockMinutes) = await LoadLoginGuardAsync();
        if (_ipGuard.IsLocked(guardKey, out var lockTo))
            throw new BusinessException($"该账号在当前网络登录失败次数过多，请于 {lockTo:HH:mm} 后重试");

        //同员工登录：_ci 排序规则下 SQL 比较不区分大小写，顾客登录也必须精确匹配
        var cCandidates = await _db.Customers.Where(c => c.LoginAccount == account).ToListAsync();
        var customer = cCandidates.FirstOrDefault(c => string.Equals(c.LoginAccount, account, StringComparison.Ordinal));
        if (customer == null)
        {
            //同员工登录
            _ = BCrypt.Net.BCrypt.Verify(dto.Password, DummyPasswordHash);
            //员工账号不在 customers 表，天然落到这里；账号不存在也记一笔，防止用不存在的用户名探测
            var locked = _ipGuard.RegisterFailure(guardKey, maxFail, lockMinutes, out _);
            await WriteLog(null, $"顾客登录失败：用户名未注册（{account}，IP {ip}）", "用户登录", "失败");
            if (locked) throw new BusinessException($"用户名或密码错误，当前网络已被封锁{lockMinutes}分钟，请稍后再试");
            throw new BusinessException($"用户名或密码错误，当前网络还剩 {_ipGuard.Remaining(guardKey, maxFail)} 次机会");
        }
        if (customer.AccountStatus == "已注销") throw new BusinessException("该账号已注销，如需继续租车请重新注册或联系门店");
        if (string.IsNullOrEmpty(customer.LoginPassword) || !BCrypt.Net.BCrypt.Verify(dto.Password, customer.LoginPassword))
        {
            var locked = _ipGuard.RegisterFailure(guardKey, maxFail, lockMinutes, out _);
            if (locked)
            {
                await WriteLog(null, $"顾客登录失败次数过多，IP {ip} 已对该账号封锁{lockMinutes}分钟（{account}）", "用户登录", "失败");
                throw new BusinessException($"用户名或密码错误，当前网络已被封锁{lockMinutes}分钟，请稍后再试");
            }
            var left = _ipGuard.Remaining(guardKey, maxFail);
            await WriteLog(null, $"顾客登录失败：密码错误，当前网络还剩 {left} 次机会（{account}，IP {ip}）", "用户登录", "失败");
            throw new BusinessException($"用户名或密码错误，当前网络还剩 {left} 次机会");
        }
        _ipGuard.Reset(guardKey);
        await WriteLog(null, $"顾客登录成功（{account}，IP {ip}）", "用户登录", "成功");

        //单点登录：顾客端同理；刷新令牌与 sid 一起登记，续期时轮换
        var refreshJti = Guid.NewGuid().ToString("N");
        var sid = await _sessions.IssueAsync(UserTypeCustomer, customer.CustomerId, refreshJti);
        var token = GenerateJwt(customer.CustomerId, customer.LoginAccount ?? account, UserTypeCustomer, RoleCustomer, sid);
        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = GenerateJwt(customer.CustomerId, customer.LoginAccount ?? account, UserTypeCustomer, RoleCustomer, sid, TokenTypeRefresh, RefreshLifetime(), refreshJti),
            UserType = UserTypeCustomer,
            //角色名固定为「顾客」，与前端菜单表逐条对应
            RoleName = RoleCustomer,
            MemberLevel = customer.MemberLevel,
            UserName = customer.FullName,
            UserId = customer.CustomerId,
            ProfileCompleted = customer.ProfileCompleted
        };
    }

    ///<summary>顾客注册：仅需用户名+密码</summary>
    public async Task<Customer> CustomerRegister(CustomerRegisterDto dto)
    {
        var account = dto.LoginAccount.Trim();
        if (!Validators.IsLoginAccount(account)) throw new BusinessException("用户名需为 3-20 位字母、数字、下划线或中文");
        if (!Validators.IsStrongPassword(dto.Password)) throw new BusinessException(Validators.PasswordRuleHint);

        if (await _db.Customers.AnyAsync(c => c.LoginAccount == account)) throw new BusinessException("该用户名已被注册");

        //姓名暂用用户名占位，租车时可由前台/顾客本人更新为真实姓名
        var customer = Customer.Register(account, null, BCrypt.Net.BCrypt.HashPassword(dto.Password));
        //身份证号与手机号在租车环节采集，注册阶段不强制
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        //账号生命周期全流程留痕
        await WriteLog(null, $"顾客注册成功（{account}，顾客ID {customer.CustomerId}）", "用户登录", "成功", customer.CustomerId);
        return customer;
    }

    ///<summary>顾客自助补全租车资料（身份证号、手机号、真实姓名）</summary>
    public async Task<Customer> UpdateCustomerProfile(int customerId, CustomerProfileDto dto)
    {
        var customer = await _db.Customers.FindAsync(customerId) ?? throw new BusinessException("客户不存在");
        if (!string.IsNullOrWhiteSpace(dto.IdCard))
        {
            var idCard = dto.IdCard.Trim();
            if (!Validators.IsIdCard(idCard)) throw new BusinessException("身份证号格式不正确");
            if (await _db.Customers.AnyAsync(c => c.IdCard == idCard && c.CustomerId != customerId)) throw new BusinessException("该身份证号已被其他账号使用");
        }
        if (!string.IsNullOrWhiteSpace(dto.ContactPhone) && !Validators.IsPhone(dto.ContactPhone.Trim())) throw new BusinessException("手机号格式不正确");

        //校验归服务层，写入归实体的领域方法（setter 已收 private）
        customer.UpdateProfile(dto.FullName, dto.IdCard, dto.ContactPhone);
        customer.BumpVersion();//顾客补资料同样参与乐观并发（与员工编辑互相感知）
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("您的资料刚被门店工作人员更新，请刷新后重试");
        }
        customer.SetMemberDiscount(await MemberDiscountLookup.OfLevelAsync(_db, customer.MemberLevel));
        return customer;
    }

    ///<summary>员工自助修改密码：临时密码首次登录与普通改密共用这一条入口</summary>
    public async Task ChangeStaffPassword(int userId, ChangePasswordDto dto)
    {
        var oldPwd = dto.OldPassword;
        var newPwd = dto.NewPassword;
        if (string.IsNullOrEmpty(oldPwd)) throw new BusinessException("请输入原密码");
        if (string.IsNullOrEmpty(newPwd)) throw new BusinessException("请输入新密码");

        var user = await _db.SystemUsers.FindAsync(userId) ?? throw new BusinessException(MsgAccountNotFound);
        if (user.UserStatus == 0) throw new BusinessException("账号已停用");
        if (!BCrypt.Net.BCrypt.Verify(oldPwd, user.LoginPwd))
        {
            await WriteLog(user.UserId, $"员工修改密码失败：原密码错误（{user.LoginAccount}）", "用户登录", "失败");
            throw new BusinessException("原密码不正确");
        }
        if (!Validators.IsStrongPassword(newPwd)) throw new BusinessException(Validators.PasswordRuleHint);
        if (BCrypt.Net.BCrypt.Verify(newPwd, user.LoginPwd)) throw new BusinessException("新密码不能与原密码相同");

        user.CompletePasswordChange(BCrypt.Net.BCrypt.HashPassword(newPwd));
        await _db.SaveChangesAsync();
        await WriteLog(user.UserId, $"员工修改登录密码成功（{user.LoginAccount}）", "用户登录", "成功");
        await _sessions.RevokeAllAsync(user.UserType, userId, "密码已修改，请用新密码重新登录");
    }

    ///<summary>顾客自助修改登录密码（个人中心入口）</summary>
    public async Task ChangeCustomerPassword(int customerId, ChangePasswordDto dto)
    {
        var oldPwd = dto.OldPassword;
        var newPwd = dto.NewPassword;
        if (string.IsNullOrEmpty(oldPwd)) throw new BusinessException("请输入原密码");
        if (string.IsNullOrEmpty(newPwd)) throw new BusinessException("请输入新密码");

        var customer = await _db.Customers.FindAsync(customerId) ?? throw new BusinessException(MsgAccountNotFound);
        if (customer.AccountStatus == "已注销") throw new BusinessException("该账号已注销");
        if (string.IsNullOrEmpty(customer.LoginPassword) || !BCrypt.Net.BCrypt.Verify(oldPwd, customer.LoginPassword))
        {
            await WriteLog(null, $"顾客修改密码失败：原密码错误（{customer.LoginAccount}）", "用户登录", "失败");
            throw new BusinessException("原密码不正确");
        }
        if (!Validators.IsStrongPassword(newPwd)) throw new BusinessException(Validators.PasswordRuleHint);
        if (BCrypt.Net.BCrypt.Verify(newPwd, customer.LoginPassword)) throw new BusinessException("新密码不能与原密码相同");

        customer.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(newPwd));
        customer.BumpVersion();//密码属于账号档案写操作，与资料编辑/注销互斥（乐观并发）
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("该账号档案刚被工作人员操作，请刷新后重试");
        }
        await WriteLog(null, $"顾客修改登录密码成功（{customer.LoginAccount}）", "用户登录", "成功");
        //改密后强制下线：凭据已变，改密前签发的 token 不应继续可用
        await _sessions.RevokeAllAsync(UserTypeCustomer, customerId, "密码已修改，请用新密码重新登录");
    }

    ///<summary>用户自主注销账号（本人操作，需二次输入密码确认）</summary>
    public async Task Deactivate(int userId, string userType, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new BusinessException("请输入当前账号密码以确认注销");

        //顾客与员工是两套档案、两套校验顺序，本来就互不相关
        if (userType == UserTypeCustomer)
        {
            await DeactivateCustomerAsync(userId, password);
            return;
        }

        await DeactivateStaffAsync(userId, password);
    }

    ///<summary>顾客自助注销：校验</summary>
    private async Task DeactivateCustomerAsync(int userId, string password)
    {
        var customer = await _db.Customers.FindAsync(userId) ?? throw new BusinessException(MsgAccountNotFound);
        if (customer.AccountStatus == "已注销") throw new BusinessException("该账号已注销");
        EnsurePasswordMatches(customer.LoginPassword, password);

        await EnsureNoUnfinishedOrdersAsync(userId);

        var oldAccount = customer.LoginAccount ?? customer.FullName;
        //注销=置状态+释放用户名与凭据+身份信息匿名化，全部收口在实体方法里
        customer.DeactivateAccount();
        customer.BumpVersion();//注销也是一次档案写操作，与编辑/等级调整互斥（乐观并发）
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("该账号档案刚被工作人员操作，请刷新后重试");
        }
        await WriteLog(null, $"顾客自助注销账号：{oldAccount}（身份信息已匿名化释放）", "系统管理", "成功");
        //账号已注销
        await _sessions.RevokeAllAsync(UserTypeCustomer, userId, "账号已注销，登录状态已失效");
    }

    ///<summary>员工自助注销：仅管理员可自助注销，且不能把最后一名管理员注销掉</summary>
    private async Task DeactivateStaffAsync(int userId, string password)
    {
        var user = await _db.SystemUsers.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId) ?? throw new BusinessException(MsgAccountNotFound);
        if (user.Role?.RoleName != RoleAdmin) throw new BusinessException("自助注销仅对「管理员」与「顾客」开放，前台/财务账号请联系管理员处理");
        if (user.UserStatus == 0) throw new BusinessException("账号已处于停用状态");
        //这里与顾客分支的密码判定分开写是有意的：员工密码列非空，不需要（也不应该）多一层空值短路
        if (!BCrypt.Net.BCrypt.Verify(password, user.LoginPwd)) throw new BusinessException("密码错误，无法注销");

        await EnsureNotLastAdminAsync();

        user.Deactivate();
        await _db.SaveChangesAsync();
        await WriteLog(user.UserId, $"管理员自助注销账号：{user.LoginAccount}", "系统管理", "成功");
        //账号已停用：清掉在线会话
        await _sessions.RevokeAllAsync(user.UserType, user.UserId, "账号已停用，登录状态已失效");
    }

    ///<summary>二次密码确认：档案里没有密码或密码不符，都按「密码错误」拒绝</summary>
    private static void EnsurePasswordMatches(string? passwordHash, string password)
    {
        //空哈希与密码不符共用同一句文案
        if (string.IsNullOrEmpty(passwordHash) || !BCrypt.Net.BCrypt.Verify(password, passwordHash))
            throw new BusinessException("密码错误，无法注销");
    }

    ///<summary>注销前置：仍有未完结订单时拒绝注销（取前 3 笔单号提示）</summary>
    private async Task EnsureNoUnfinishedOrdersAsync(int customerId)
    {
        var unfinished = await _db.RentalOrders
            .Where(o => o.CustomerId == customerId && o.OrderStatus != "已完成" && o.OrderStatus != "已完结" && o.OrderStatus != "已取消")
            .Select(o => o.OrderNo)
            .ToListAsync();
        if (unfinished.Count > 0)
            throw new BusinessException($"您还有 {unfinished.Count} 笔未完结订单（{string.Join("、", unfinished.Take(3))}），请先完成归还结算再注销");
    }

    ///<summary>注销前置</summary>
    private async Task EnsureNotLastAdminAsync()
    {
        var adminCount = await _db.SystemUsers.CountAsync(u => u.UserStatus != 0 && u.Role!.RoleName == RoleAdmin);
        if (adminCount <= 1) throw new BusinessException("当前账号是系统中最后一名管理员，注销后将无人可管理系统，请先新增其他管理员");
    }

    ///<summary>员工关键操作的二次密码校验</summary>
    public async Task VerifyStaffPassword(int userId, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new BusinessException("请输入当前管理员的登录密码以确认本次操作");
        var user = await _db.SystemUsers.FirstOrDefaultAsync(u => u.UserId == userId) ?? throw new BusinessException(MsgAccountNotFound);
        if (string.IsNullOrEmpty(user.LoginPwd) || !BCrypt.Net.BCrypt.Verify(password, user.LoginPwd))
        {
            await WriteLog(userId, $"关键操作二次校验失败：密码错误（{user.LoginAccount}）", "车辆管理", "失败");
            throw new BusinessException("密码不正确，无法执行该操作");
        }
    }

    ///<summary>退出登录：撤销当前这一个会话（单点登录登记表里的那条）</summary>
    public async Task Logout(string userType, int userId, string? sessionToken) => await _sessions.RevokeAsync(userType, userId, sessionToken);

    private async Task WriteLog(int? userId, string content, string module, string result, int? customerId = null)
    {
        //审计日志要能追溯「谁、在什么时间、从哪台机器」做了什么
        _db.SystemLogs.Add(SystemLog.Create(userId, customerId, content, module, result, ResolveClientIp()));
        await _db.SaveChangesAsync();
    }

    ///<summary>当前请求的客户端 IP</summary>
    private string? ResolveClientIp() => ClientIp.Resolve(_http.HttpContext);

    private string GenerateJwt(int userId, string account, string userType, string role, string sessionToken, string tokenType = TokenTypeAccess, TimeSpan? lifetime = null, string? refreshJti = null, bool mustChangePassword = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, account),
            new("user_type", userType),
            new(ClaimTypes.Role, role),
            //sid：本次登录的会话标识
            new(Middleware.SingleSessionMiddleware.SessionClaim, sessionToken),
            //token_type：区分「访问令牌」与「续期令牌」
            new(TokenTypeClaim, tokenType)
        ];
        //jti：续期令牌的一次性标识，刷新时轮换（旧令牌立即作废）
        if (!string.IsNullOrEmpty(refreshJti)) claims.Add(new(JwtRegisteredClaimNames.Jti, refreshJti));
        if (mustChangePassword) claims.Add(new("must_change", "1"));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: AppClock.Now.Add(lifetime ?? AccessLifetime()),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    ///<summary>访问令牌 Cookie 的默认有效期（分钟），与 Jwt:ExpireMinutes 的默认值保持一致</summary>
    public const int AccessTokenMinutes = 120;

    ///<summary>续期令牌 Cookie 的默认有效期（天），与 Jwt:RefreshExpireDays 的默认值保持一致</summary>
    public const int RefreshTokenDays = 7;

    ///<summary>访问令牌有效期（配置 Jwt:ExpireMinutes，默认 120 分钟）</summary>
    private TimeSpan AccessLifetime() => TimeSpan.FromMinutes(ReadLifetime("Jwt:ExpireMinutes", AccessTokenMinutes));

    ///<summary>续期令牌有效期（配置 Jwt:RefreshExpireDays，默认 7 天）</summary>
    private TimeSpan RefreshLifetime() => TimeSpan.FromDays(ReadLifetime("Jwt:RefreshExpireDays", 7));

    ///<summary>
    /// 读取时长配置：固定用不变文化解析。服务器区域设置若把小数点当千分位，
    /// double.Parse("0.5") 会得到 5，令牌有效期就被悄悄放大了 10 倍。
    /// </summary>
    private double ReadLifetime(string key, double fallback) =>
        double.TryParse(_config[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

    ///<summary>用续期令牌换一对新令牌</summary>
    public async Task<LoginResponseDto> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new BusinessException("登录状态已失效，请重新登录");

        var principal = ValidateRefreshToken(refreshToken);
        var rawId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var account = principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var userType = principal.FindFirst("user_type")?.Value ?? "staff";
        var sid = principal.FindFirst(Middleware.SingleSessionMiddleware.SessionClaim)?.Value;
        var refreshJti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        if (!int.TryParse(rawId, out var userId) || string.IsNullOrEmpty(sid)) throw new BusinessException("登录状态已失效，请重新登录");
        //升级前签发的续期令牌没有 jti，无法参与轮换：要求重新登录一次，避免旧令牌继续可重放
        if (string.IsNullOrEmpty(refreshJti)) throw new BusinessException("登录状态已失效，请重新登录");

        //与单点登录接缝：sid 与 jti 都必须匹配登记表，且读库绕过缓存（旧令牌换出后立即失效）
        var (sessionOk, revokedReason) = await _sessions.CheckRefreshAsync(userType, userId, sid, refreshJti);
        if (!sessionOk) throw new BusinessException(revokedReason ?? "该账号已在其他设备登录，当前登录已被下线");

        //先轮换再签发：并发重放时条件更新只有一个请求能成功，另一个直接判失效
        var newJti = Guid.NewGuid().ToString("N");
        await _sessions.RotateRefreshAsync(userType, userId, sid, refreshJti, newJti);

        if (userType == UserTypeCustomer)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == userId) ?? throw new BusinessException("账号不存在，请重新登录");
            if (customer.AccountStatus == "已注销") throw new BusinessException("该账号已注销，请重新登录");

            return new LoginResponseDto
            {
                //账号名以库为准
                Token = GenerateJwt(userId, customer.LoginAccount ?? account, UserTypeCustomer, RoleCustomer, sid),
                RefreshToken = GenerateJwt(userId, customer.LoginAccount ?? account, UserTypeCustomer, RoleCustomer, sid, TokenTypeRefresh, RefreshLifetime(), newJti),
                UserType = UserTypeCustomer,
                RoleName = RoleCustomer,
                //姓名可能为空（注册只填用户名），为空时回落到令牌里的账号名
                UserName = string.IsNullOrEmpty(customer.FullName) ? account : customer.FullName,
                UserId = userId,
                MemberLevel = customer.MemberLevel,
                //复用模型上的计算属性
                ProfileCompleted = customer.ProfileCompleted
            };
        }

        var user = await _db.SystemUsers.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId) ?? throw new BusinessException("账号不存在，请重新登录");
        if (user.UserStatus != 1) throw new BusinessException("该账号已被停用，请重新登录");

        var roleName = user.Role?.RoleName ?? RoleReception;
        return new LoginResponseDto
        {
            Token = GenerateJwt(userId, user.LoginAccount, user.UserType, roleName, sid, mustChangePassword: user.MustChangePassword),
            RefreshToken = GenerateJwt(userId, user.LoginAccount, user.UserType, roleName, sid, TokenTypeRefresh, RefreshLifetime(), newJti, user.MustChangePassword),
            UserType = user.UserType,
            RoleName = roleName,
            PermissionCodes = await LoadPermissionCodesAsync(user.Role),
            UserName = user.UserName ?? user.LoginAccount,
            UserId = userId,
            ProfileCompleted = true,
            MustChangePassword = user.MustChangePassword,
            PasswordResetExpire = user.PwdResetExpire
        };
    }

    ///<summary>校验续期令牌的签名与有效期，并确认它确实是 refresh 类型</summary>
    private ClaimsPrincipal ValidateRefreshToken(string refreshToken)
    {
        var handler = new JwtSecurityTokenHandler();
        //先判可读性：格式非法的串交给 ValidateToken 可能抛 ArgumentException，
        //与「签名/有效期校验失败」混在一起，反而不利于定位。
        if (!handler.CanReadToken(refreshToken)) throw new BusinessException("登录状态已失效，请重新登录");

        //参数在 try 外构造：Jwt:Key 缺失属于部署配置错误，应让 500 暴露出来，
        //不能被下面的「令牌无效」catch 吞成 401。
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _config["Jwt:Issuer"],
            ValidAudience = _config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!))
        };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(refreshToken, validation, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            //签名错误/已过期/颁发者不符/报文结构非法：统一收敛为同一句人话，不向客户端暴露具体校验环节
            throw new BusinessException("登录状态已失效，请重新登录");
        }

        //只接受 refresh 类型。放在 try 之外：类型不符是业务拒绝，不是「令牌损坏」，
        //也不该再用异常消息字符串做控制流（改文案就会把 catch 条件改坏）。
        return principal.FindFirst(TokenTypeClaim)?.Value == TokenTypeRefresh
            ? principal
            : throw new BusinessException("该令牌不能用于续期，请重新登录");
    }
}
