using ERental.Authorization;
using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class AddonController(AddonService svc) : ControllerBase
{
    private readonly AddonService _svc = svc;

    ///<summary>增值服务列表：下单页只取启用项，运营页取全部</summary>
    [HttpGet]
    [Authorize(Policy = Policies.AdminOrReceptionOrCustomer)]
    public async Task<IActionResult> GetAddons(bool onlyEnabled = false) =>
        Ok(new ApiResponse<List<AddonDto>> { Data = await _svc.GetAllAsync(onlyEnabled) });

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> CreateAddon([FromBody] AddonInput input)
    {
        try { return Ok(new ApiResponse<AddonDto> { Data = await _svc.CreateAsync(input), Message = "增值服务已创建" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>上传服务图片：返回 /uploads/addon/… 相对 URL，保存时随 AddonInput.ImageUrl 回传</summary>
    [HttpPost("image")]
    [Authorize(Policy = Policies.AdminOnly)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        try
        {
            var url = await _svc.SaveImageAsync(file);
            return Ok(new ApiResponse<object> { Data = new { url }, Message = "图片已上传" });
        }
        catch (Exception ex) when (ErrorText.IsHandled(ex))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> UpdateAddon(int id, [FromBody] AddonInput input)
    {
        try { return Ok(new ApiResponse<AddonDto> { Data = await _svc.UpdateAsync(id, input), Message = "增值服务已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    [HttpPut("{id:int}/status")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> SetAddonStatus(int id, [FromBody] ChangeStatusDto dto)
    {
        try { await _svc.SetStatusAsync(id, dto.Status); return Ok(new ApiResponse<object> { Message = "状态已更新" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }
}
