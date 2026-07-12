using FoodDelivery.Api.DTOs;
using FoodDelivery.Api.Services;

namespace FoodDelivery.Api.Controllers;

public class CategoriesController : BaseCrudController<CategoryDto, CreateCategoryDto, UpdateCategoryDto, string>
{
    public CategoriesController(IBaseService<CategoryDto, CreateCategoryDto, UpdateCategoryDto, string> service)
        : base(service)
    {
    }
}
