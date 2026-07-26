using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FocusAI.Application.Common.Interfaces;

namespace FocusAI.Api.Security;

/// <summary>Reads the caller's identity off the validated JWT on the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            // JwtSecurityTokenHandler remaps "sub" to NameIdentifier unless the
            // default inbound map is cleared; accept either so the property does
            // not silently break if that setting changes.
            var raw = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email =>
        Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
