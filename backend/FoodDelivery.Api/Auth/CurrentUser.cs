using System.Security.Claims;
using FoodDelivery.Application.Abstractions;

namespace FoodDelivery.Api.Auth;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier);
    public Guid? TenantId => ReadGuid("tenant_id");
    public bool IsInRole(string role) => _accessor.HttpContext?.User.IsInRole(role) == true;

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(_accessor.HttpContext?.User.FindFirstValue(claimType), out var value) ? value : null;
}
