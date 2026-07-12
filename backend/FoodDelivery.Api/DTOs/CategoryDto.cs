namespace FoodDelivery.Api.DTOs;

public class CategoryDto
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Icon { get; set; } = default!;
}

public class CreateCategoryDto
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Icon { get; set; } = default!;
}

public class UpdateCategoryDto
{
    public string Name { get; set; } = default!;
    public string Icon { get; set; } = default!;
}
