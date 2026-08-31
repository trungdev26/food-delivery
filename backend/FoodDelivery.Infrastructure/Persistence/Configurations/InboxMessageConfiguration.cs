using FoodDelivery.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodDelivery.Infrastructure.Persistence.Configurations;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(x => new { x.ConsumerName, x.TenantId, x.ShopId, x.MessageId });
        builder.Property(x => x.ConsumerName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProcessedAtUtc).HasColumnType("datetime(6)").IsRequired();
    }
}
