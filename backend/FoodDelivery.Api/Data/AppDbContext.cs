using FoodDelivery.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasMaxLength(50);
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
            entity.Property(c => c.Icon).HasMaxLength(20).IsRequired();

            entity.HasData(
                new Category { Id = "com", Name = "Cơm", Icon = "🍚" },
                new Category { Id = "pho-bun", Name = "Phở & Bún", Icon = "🍜" },
                new Category { Id = "pizza", Name = "Pizza", Icon = "🍕" },
                new Category { Id = "ga-ran", Name = "Gà rán", Icon = "🍗" },
                new Category { Id = "tra-sua", Name = "Trà sữa", Icon = "🧋" },
                new Category { Id = "an-vat", Name = "Ăn vặt", Icon = "🍢" },
                new Category { Id = "do-chay", Name = "Đồ chay", Icon = "🥗" },
                new Category { Id = "trang-mieng", Name = "Tráng miệng", Icon = "🍰" }
            );
        });
    }
}
