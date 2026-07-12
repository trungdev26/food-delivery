using FoodDelivery.Api.DTOs;
using FoodDelivery.Api.Entities;
using FoodDelivery.Api.Repositories;

namespace FoodDelivery.Api.Services;

public class CategoryService : BaseService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto, string>
{
    public CategoryService(IRepository<Category, string> repository) : base(repository)
    {
    }

    protected override CategoryDto ToDto(Category entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Icon = entity.Icon,
    };

    protected override Category ToEntity(CreateCategoryDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Icon = dto.Icon,
    };

    protected override void ApplyUpdate(Category entity, UpdateCategoryDto dto)
    {
        entity.Name = dto.Name;
        entity.Icon = dto.Icon;
    }
}
