namespace FoodDelivery.Application.Abstractions;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
    void Set(Guid tenantId);
}
