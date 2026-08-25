using FoodDelivery.Infrastructure.Tenancy;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void Tenant_id_is_unavailable_before_resolution()
    {
        var context = new TenantContext();
        Assert.Throws<InvalidOperationException>(() => context.TenantId);
    }

    [Fact]
    public void Tenant_cannot_change_during_request()
    {
        var context = new TenantContext();
        context.Set(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => context.Set(Guid.NewGuid()));
    }
}
