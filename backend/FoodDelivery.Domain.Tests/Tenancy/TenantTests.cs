using FoodDelivery.Domain.Tenancy;
using Xunit;

namespace FoodDelivery.Domain.Tests.Tenancy;

public class TenantTests
{
    [Fact]
    public void Create_normalizes_subdomain_and_activates_tenant()
    {
        var tenant = Tenant.Create(Guid.NewGuid(), "Bánh Mỳ Cay", " BanhMyCay ");

        Assert.Equal("banhmycay", tenant.Subdomain);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-starts-with-hyphen")]
    public void Create_rejects_invalid_subdomain(string subdomain)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create(Guid.NewGuid(), "Tenant", subdomain));
    }
}
