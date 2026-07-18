using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetAuth0.Application.Abstractions;
using NetAuth0.Application.Admin;

namespace NetAuth0.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin")]
public sealed class AdminController(IAdminService admin, IKeyManagementService keys, RequestContext requestContext) : ControllerBase
{
    [HttpGet("applications")]
    [Authorize(Policy = "applications:manage")]
    public async Task<IActionResult> Applications([FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        return Ok(await admin.ListApplicationsAsync(tenantId, cancellationToken));
    }

    [HttpPost("applications")]
    [Authorize(Policy = "applications:manage")]
    public async Task<IActionResult> CreateApplication(CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        return Ok(await admin.CreateApplicationAsync(request, requestContext, cancellationToken));
    }

    [HttpGet("users")]
    [Authorize(Policy = "users:manage")]
    public async Task<IActionResult> Users([FromQuery] Guid tenantId, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        return Ok(await admin.SearchUsersAsync(tenantId, q, page, pageSize, cancellationToken));
    }

    [HttpPost("users/{userId:guid}/block")]
    [Authorize(Policy = "users:manage")]
    public async Task<IActionResult> BlockUser(Guid userId, [FromQuery] Guid tenantId, [FromQuery] bool blocked = true, CancellationToken cancellationToken = default)
    {
        await admin.SetUserBlockedAsync(tenantId, userId, blocked, requestContext, cancellationToken);
        return NoContent();
    }

    [HttpPost("users/{userId:guid}/force-logout")]
    [Authorize(Policy = "users:manage")]
    public async Task<IActionResult> ForceLogout(Guid userId, [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        await admin.ForceLogoutAsync(tenantId, userId, requestContext, cancellationToken);
        return NoContent();
    }

    [HttpPost("roles")]
    [Authorize(Policy = "roles:manage")]
    public async Task<IActionResult> CreateRole(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        return Ok(await admin.CreateRoleAsync(request, requestContext, cancellationToken));
    }

    [HttpPost("roles/assignments")]
    [Authorize(Policy = "roles:manage")]
    public async Task<IActionResult> AssignRole(AssignRoleRequest request, CancellationToken cancellationToken)
    {
        await admin.AssignRoleAsync(request, requestContext, cancellationToken);
        return NoContent();
    }

    [HttpGet("audit-events")]
    [Authorize(Policy = "audit:read")]
    public async Task<IActionResult> Audit([FromQuery] Guid tenantId, [FromQuery] string? eventType, [FromQuery] string? actor, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return Ok(await admin.SearchAuditEventsAsync(tenantId, eventType, actor, page, pageSize, cancellationToken));
    }

    [HttpPost("signing-keys/rotate")]
    [Authorize(Policy = "applications:manage")]
    public async Task<IActionResult> RotateSigningKey(CancellationToken cancellationToken)
    {
        return Ok(new { keyId = await keys.RotateAsync(cancellationToken) });
    }
}
