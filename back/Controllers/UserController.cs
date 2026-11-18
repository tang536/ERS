using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using ERental.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.AdminOnly)]
public class UserController(UserService svc) : ControllerBase
{
    private readonly UserService _svc = svc;

    /// <summary>取当前登录管理员 id（JWT 中的 NameIdentifier）</summary>
    private int CurrentAdminId() =>
        int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    /// <summary>敏感操作统一先校验当前管理员密码；每次操作都要重新确认，不复用上一次的结果</summary>
    private async Task GuardAsync(string? adminPassword) =>
        await _svc.VerifyAdminPassword(CurrentAdminId(), adminPassword);

    [HttpGet]
    public async Task<IActionResult> QueryStaffAccounts() => Ok(new ApiResponse<List<UserDto>> { Data = await _svc.GetAllStaff() });

    [HttpPost]
    public async Task<IActionResult> AddStaffAccount(
        [FromBody] CreateUserDto dto,
        [FromHeader(Name = "X-Admin-Password")] string? adminPassword)
    {
        try
        {
            await GuardAsync(adminPassword);
            return Ok(new ApiResponse<UserDto> { Data = await _svc.CreateStaff(dto) });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> EditStaffAccount(
        int id, [FromBody] UpdateUserDto dto,
        [FromHeader(Name = "X-Admin-Password")] string? adminPassword)
    {
        try { await GuardAsync(adminPassword); await _svc.UpdateStaff(id, dto); return Ok(new ApiResponse<object> { Message = "更新成功" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>管理员生成一次性临时密码；明文只在本次响应返回，前端展示后不再保存</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetStaffPassword(
        int id, [FromHeader(Name = "X-Admin-Password")] string? adminPassword)
    {
        try { await GuardAsync(adminPassword); return Ok(new ApiResponse<ResetPasswordResultDto> { Data = await _svc.ResetPasswordAsync(id), Message = "临时密码已生成" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>停用员工账号：置为停用并立即吊销该账号全部在线会话，旧令牌随即失效</summary>
    [HttpPut("{id:int}/disable")]
    public async Task<IActionResult> DisableStaffAccount(
        int id, [FromHeader(Name = "X-Admin-Password")] string? adminPassword)
    {
        try
        {
            await GuardAsync(adminPassword);
            await _svc.UpdateStaff(id, new UpdateUserDto { UserStatus = 0 });
            return Ok(new ApiResponse<object> { Message = "账号已停用" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteStaffAccount(
        int id, [FromHeader(Name = "X-Admin-Password")] string? adminPassword)
    {
        try { await GuardAsync(adminPassword); await _svc.DeleteStaff(id); return Ok(new ApiResponse<object> { Message = "删除成功" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }
}
