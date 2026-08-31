using FoodDelivery.Application.Abstractions;
using FoodDelivery.Infrastructure.Persistence;
using FoodDelivery.Infrastructure.Tenancy;
using FoodDelivery.Infrastructure.Events;
using FoodDelivery.Application.Abstractions.Messaging;
using FoodDelivery.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 21))));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUnitOfWorkFactory>(provider =>
            new EfDapperUnitOfWorkFactory(
                connectionString,
                provider.GetRequiredService<IDomainEventDispatcher>()));

        var rabbitMqOptions = configuration
            .GetSection(RabbitMqOptions.SectionName)
            .Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        rabbitMqOptions.EnsureValid();
        services.AddSingleton(rabbitMqOptions);
        services.AddSingleton<RabbitMqConnectionManager>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddSingleton(provider => new OutboxDispatcher(
            connectionString,
            provider.GetRequiredService<RabbitMqPublisher>(),
            rabbitMqOptions,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxDispatcher>>()));
        services.AddHostedService(provider => provider.GetRequiredService<OutboxDispatcher>());
        services.AddHostedService<RabbitMqConsumerHostedService>();
        services.AddSingleton<IIntegrationEventPublisher>(provider =>
            provider.GetRequiredService<RabbitMqPublisher>());
        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: new[] { "ready" });

        return services;
    }
}
