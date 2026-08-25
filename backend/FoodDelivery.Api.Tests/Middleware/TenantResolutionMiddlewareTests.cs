using FoodDelivery.Api.Middleware;
using FoodDelivery.Api.Options;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Domain.Tenancy;
using FoodDelivery.Infrastructure.Persistence;
using FoodDelivery.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FoodDelivery.Api.Tests.Middleware;

public class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task Resolves_active_tenant_from_subdomain()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options, new NoOpDispatcher());
        var tenant = Tenant.Create(Guid.NewGuid(), "Bánh Mỳ Cay", "banhmycay");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("banhmycay.food.com.vn");
        var tenantContext = new TenantContext();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            http,
            new AnonymousCurrentUser(),
            tenantContext,
            db,
            Microsoft.Extensions.Options.Options.Create(new TenantResolutionOptions
            {
                BaseDomain = "food.com.vn",
                SharedHosts = new[] { "live.food.com.vn", "localhost" },
            }));

        Assert.Equal(tenant.Id, tenantContext.TenantId);
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public bool IsInRole(string role) => false;
    }

    private sealed class NoOpDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
