using Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure;

public static class CatalogSeedData
{
    public static async Task InitializeAsync(CatalogDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        if (await dbContext.Products.AnyAsync(cancellationToken)) return;

        dbContext.Products.AddRange(
            Product.Create("Solstice Linen Shirt", "solstice-linen-shirt", "Breathable European linen with a relaxed, tailored silhouette.", "Apparel", 89.00m),
            Product.Create("Mori Ceramic Set", "mori-ceramic-set", "Hand-finished stoneware cups designed for slow mornings.", "Home", 64.00m),
            Product.Create("Field Notes No. 07", "field-notes-no-07", "A stitched, 160-page notebook for ideas that deserve room.", "Stationery", 18.00m),
            Product.Create("Arc Leather Tote", "arc-leather-tote", "Full-grain leather carryall with a structured everyday profile.", "Accessories", 148.00m));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}