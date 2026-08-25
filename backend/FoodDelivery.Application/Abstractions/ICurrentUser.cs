namespace FoodDelivery.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    bool IsInRole(string role);
}
