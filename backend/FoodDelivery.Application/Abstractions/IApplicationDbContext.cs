using FoodDelivery.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
