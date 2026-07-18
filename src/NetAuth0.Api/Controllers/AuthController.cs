using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Identity;

namespace NetAuth0.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IIdentityService identity, RequestContext requestContext) : ControllerBase
{
    [HttpPost("signup")]
    [EnableRateLimiting("public-auth")]
    public async Task<IActionResult> Signup(SignupRequest request, CancellationToken cancellationToken)
    {
        var id = await identity.SignupAsync(request, requestContext, cancellationToken);
        return Created($"/api/v1/users/{id}", new { id });
    }

    [HttpPost("login")]
    [EnableRateLimiting("public-auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await identity.LoginAsync(request, requestContext, cancellationToken));
    }

    [Authorize]
    [HttpPost("mfa/totp")]
    public async Task<ActionResult<MfaEnrollmentResponse>> BeginMfa(CancellationToken cancellationToken)
    {
        return Ok(await identity.BeginTotpEnrollmentAsync(CurrentUserId(), cancellationToken));
    }

    [Authorize]
    [HttpPost("mfa/totp/verify")]
    public async Task<IActionResult> VerifyMfa(VerifyMfaRequest request, CancellationToken cancellationToken)
    {
        await identity.VerifyTotpEnrollmentAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken cancellationToken)
    {
        return Ok(await identity.GetSessionsAsync(CurrentUserId(), cancellationToken));
    }

    [Authorize]
    [HttpDelete("sessions/{familyId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid familyId, CancellationToken cancellationToken)
    {
        await identity.RevokeSessionFamilyAsync(CurrentUserId(), familyId, requestContext, cancellationToken);
        return NoContent();
    }

    private Guid CurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.Parse(sub!);
    }
}
