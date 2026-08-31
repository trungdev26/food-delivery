using FoodDelivery.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDelivery.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RoutingKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("json").IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.LockedBy).HasMaxLength(100);
        builder.Property(x => x.LockedUntilUtc).HasColumnType("datetime(6)");
        builder.Property(x => x.SentAtUtc).HasColumnType("datetime(6)");
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.HasIndex(x => new { x.SentAtUtc, x.LockedUntilUtc, x.OccurredAtUtc });
    }
}
