using System.Data;
using FoodDelivery.Api.Common;
using FoodDelivery.Api.Data;
using FoodDelivery.Api.DTOs;
using FoodDelivery.Api.Entities;
using FoodDelivery.Api.Repositories;
using FoodDelivery.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default in configuration.");

// Add services to the container.

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            return new BadRequestObjectResult(new ApiErrorPayload
            {
                Message = "Dữ liệu không hợp lệ",
                Code = "ERR_VALIDATION",
                Errors = errors,
            });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 21))));

builder.Services.AddScoped<IDbConnection>(_ => new MySqlConnection(connectionString));

builder.Services.AddScoped<IRepository<Category, string>, CategoryRepository>();
builder.Services.AddScoped<IBaseService<CategoryDto, CreateCategoryDto, UpdateCategoryDto, string>, CategoryService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    try
    {
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not apply database migrations. Is MySQL running and reachable?");
    }
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();
