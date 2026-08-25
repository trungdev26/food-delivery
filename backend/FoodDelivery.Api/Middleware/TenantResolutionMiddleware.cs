using FoodDelivery.Api.Options;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Tenancy;
using FoodDelivery.Infrastructure.Persistence;
using FoodDelivery.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FoodDelivery.Api.Middleware;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        TenantContext tenantContext,
        ApplicationDbContext dbContext,
        IOptions<TenantResolutionOptions> options)
    {
        var settings = options.Value;
        Tenant? tenant = null;

        if (currentUser.TenantId is Guid tenantId)
        {
            tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                x => x.Id == tenantId && x.Status == TenantStatus.Active,
                httpContext.RequestAborted);
        }
        else
        {
            var host = httpContext.Request.Host.Host.ToLowerInvariant();
            if (settings.SharedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                await _next(httpContext);
                return;
            }

            var suffix = "." + settings.BaseDomain.ToLowerInvariant();
            var subdomain = host.EndsWith(suffix, StringComparison.Ordinal)
                ? host[..^suffix.Length]
                : string.Empty;

            if (!subdomain.Contains('.'))
            {
                tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                    x => x.Subdomain == subdomain && x.Status == TenantStatus.Active,
                    httpContext.RequestAborted);
            }
        }

        if (tenant is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        tenantContext.Set(tenant.Id);
        await _next(httpContext);
    }
}
