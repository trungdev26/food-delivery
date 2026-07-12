namespace FoodDelivery.Api.Services;

public interface IBaseService<TDto, TCreateDto, TUpdateDto, TKey>
{
    Task<IEnumerable<TDto>> GetAllAsync();

    Task<TDto?> GetByIdAsync(TKey id);

    Task<TDto> CreateAsync(TCreateDto dto);

    Task<TDto?> UpdateAsync(TKey id, TUpdateDto dto);

    Task<bool> DeleteAsync(TKey id);
}
