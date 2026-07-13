using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyoTrack.Api.Controllers;

[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Token sem identificador de usuário."));
}
