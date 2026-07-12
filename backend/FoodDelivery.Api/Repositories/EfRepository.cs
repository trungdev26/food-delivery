using FoodDelivery.Api.Data;
using FoodDelivery.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Api.Repositories;

public abstract class EfRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<TEntity> DbSet;

    protected EfRepository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        return await DbSet.ToListAsync();
    }

    public virtual async Task<TEntity?> GetByIdAsync(TKey id)
    {
        return await DbSet.FindAsync(id);
    }

    public virtual async Task AddAsync(TEntity entity)
    {
        DbSet.Add(entity);
        await Context.SaveChangesAsync();
    }

    public virtual async Task UpdateAsync(TEntity entity)
    {
        DbSet.Update(entity);
        await Context.SaveChangesAsync();
    }

    public virtual async Task<bool> DeleteAsync(TKey id)
    {
        var entity = await DbSet.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        DbSet.Remove(entity);
        await Context.SaveChangesAsync();
        return true;
    }
}
