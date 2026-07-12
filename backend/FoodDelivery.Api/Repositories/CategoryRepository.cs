using System.Data;
using Dapper;
using FoodDelivery.Api.Data;
using FoodDelivery.Api.Entities;

namespace FoodDelivery.Api.Repositories;

public class CategoryRepository : EfRepository<Category, string>
{
    private readonly IDbConnection _connection;

    public CategoryRepository(AppDbContext context, IDbConnection connection) : base(context)
    {
        _connection = connection;
    }

    public override async Task<IEnumerable<Category>> GetAllAsync()
    {
        return await _connection.QueryAsync<Category>(
            "SELECT id AS Id, name AS Name, icon AS Icon FROM categories");
    }

    public override async Task<Category?> GetByIdAsync(string id)
    {
        return await _connection.QueryFirstOrDefaultAsync<Category>(
            "SELECT id AS Id, name AS Name, icon AS Icon FROM categories WHERE id = @Id",
            new { Id = id });
    }
}
