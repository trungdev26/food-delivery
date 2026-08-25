using FoodDelivery.Application.Abstractions;

namespace FoodDelivery.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("Tenant has not been resolved.");
    public bool HasTenant => _tenantId.HasValue;

    public void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (_tenantId.HasValue && _tenantId != tenantId)
            throw new InvalidOperationException("Tenant cannot change during a request.");

        _tenantId = tenantId;
    }
}
