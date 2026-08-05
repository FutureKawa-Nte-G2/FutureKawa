using System.Security.Claims;

namespace FutureKawaSiege.Commons.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("sub")?.Value;
        return Guid.Parse(claim!);
    }

    public static string GetEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Email)?.Value
               ?? principal.FindFirst("email")?.Value
               ?? throw new InvalidOperationException("Email claim not found.");
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Role)?.Value
               ?? principal.FindFirst("role")?.Value
               ?? throw new InvalidOperationException("Role claim not found.");
    }

    public static int? GetWarehouseId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("warehouse_id")?.Value;
        return claim is not null ? int.Parse(claim) : null;
    }
}
