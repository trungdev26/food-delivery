namespace FoodDelivery.Api.Entities;

public class Category : IEntity<string>
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Icon { get; set; } = default!;
}
