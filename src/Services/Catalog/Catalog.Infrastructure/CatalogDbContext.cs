using Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();
        product.ToTable("products");
        product.HasKey(item => item.Id);
        product.Property(item => item.Name).HasMaxLength(200).IsRequired();
        product.Property(item => item.Slug).HasMaxLength(200).IsRequired();
        product.HasIndex(item => item.Slug).IsUnique();
        product.Property(item => item.Description).HasMaxLength(2000).IsRequired();
        product.Property(item => item.Category).HasMaxLength(100).IsRequired();
        product.HasIndex(item => item.Category);
        product.Property(item => item.Price).HasPrecision(18, 2).IsRequired();
        product.Property(item => item.Currency).HasMaxLength(3).IsRequired();
        product.Property(item => item.IsActive).IsRequired();
    }
}