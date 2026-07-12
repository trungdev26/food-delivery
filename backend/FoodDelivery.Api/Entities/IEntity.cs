namespace FoodDelivery.Api.Entities;

public interface IEntity<TKey>
{
    TKey Id { get; set; }
}
