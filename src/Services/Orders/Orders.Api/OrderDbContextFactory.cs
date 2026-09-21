using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql("Host=localhost;Port=5432;Database=orders;Username=ecommerce;Password=ecommerce_dev_only").Options);
}