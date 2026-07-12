using FoodDelivery.Api.Common;
using FoodDelivery.Api.Entities;
using FoodDelivery.Api.Repositories;

namespace FoodDelivery.Api.Services;

public abstract class BaseService<TEntity, TDto, TCreateDto, TUpdateDto, TKey>
    : IBaseService<TDto, TCreateDto, TUpdateDto, TKey>
    where TEntity : class, IEntity<TKey>
{
    protected readonly IRepository<TEntity, TKey> Repository;

    protected BaseService(IRepository<TEntity, TKey> repository)
    {
        Repository = repository;
    }

    protected abstract TDto ToDto(TEntity entity);

    protected abstract TEntity ToEntity(TCreateDto dto);

    protected abstract void ApplyUpdate(TEntity entity, TUpdateDto dto);

    public virtual async Task<IEnumerable<TDto>> GetAllAsync()
    {
        var entities = await Repository.GetAllAsync();
        return entities.Select(ToDto);
    }

    public virtual async Task<TDto?> GetByIdAsync(TKey id)
    {
        var entity = await Repository.GetByIdAsync(id);
        return entity is null ? default : ToDto(entity);
    }

    public virtual async Task<TDto> CreateAsync(TCreateDto dto)
    {
        var entity = ToEntity(dto);

        var existing = await Repository.GetByIdAsync(entity.Id);
        if (existing is not null)
        {
            throw new ApiException(StatusCodes.Status409Conflict, $"'{entity.Id}' đã tồn tại", "ERR_DUPLICATE_ID");
        }

        await Repository.AddAsync(entity);
        return ToDto(entity);
    }

    public virtual async Task<TDto?> UpdateAsync(TKey id, TUpdateDto dto)
    {
        var entity = await Repository.GetByIdAsync(id);
        if (entity is null)
        {
            return default;
        }

        ApplyUpdate(entity, dto);
        await Repository.UpdateAsync(entity);
        return ToDto(entity);
    }

    public virtual async Task<bool> DeleteAsync(TKey id)
    {
        return await Repository.DeleteAsync(id);
    }
}
