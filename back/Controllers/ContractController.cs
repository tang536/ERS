using System.Security.Claims;
using ERental.Authorization;
using ERental.DTOs;
using ERental.Models;
using ERental.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERental.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.CustomerOnly)]
public class ContractController(ContractService svc) : ControllerBase
{
    private readonly ContractService _svc = svc;
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    ///<summary>查看合同：已签署返回留档正文，未签署返回按当前订单生成的预览</summary>
    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetContract(int orderId)
    {
        try { return Ok(new ApiResponse<ContractDto> { Data = await _svc.GetAsync(orderId, CurrentUserId) }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }

    ///<summary>手写签名并留档（同一订单只能签一次）</summary>
    [HttpPost("sign")]
    public async Task<IActionResult> SignContract([FromBody] SignContractInput input)
    {
        try { return Ok(new ApiResponse<ContractDto> { Data = await _svc.SignAsync(input, CurrentUserId), Message = "合同已签署" }); }
        catch (Exception ex) when (ErrorText.IsHandled(ex)) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ErrorText.Safe(ex), Success = false }); }
    }
}
