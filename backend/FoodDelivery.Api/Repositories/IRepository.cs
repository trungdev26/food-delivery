using FoodDelivery.Api.Entities;

namespace FoodDelivery.Api.Repositories;

public interface IRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    Task<IEnumerable<TEntity>> GetAllAsync();

    Task<TEntity?> GetByIdAsync(TKey id);

    Task AddAsync(TEntity entity);

    Task UpdateAsync(TEntity entity);

    Task<bool> DeleteAsync(TKey id);
}
