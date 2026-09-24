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
            Product.Create("Solstice Linen Shirt", "solstice-linen-shirt", "Breathable European linen with a relaxed, tailored silhouette.", "Apparel", 89.00m, id: DemoProductIds.SolsticeLinenShirt),
            Product.Create("Mori Ceramic Set", "mori-ceramic-set", "Hand-finished stoneware cups designed for slow mornings.", "Home", 64.00m, id: DemoProductIds.MoriCeramicSet),
            Product.Create("Field Notes No. 07", "field-notes-no-07", "A stitched, 160-page notebook for ideas that deserve room.", "Stationery", 18.00m, id: DemoProductIds.FieldNotes),
            Product.Create("Arc Leather Tote", "arc-leather-tote", "Full-grain leather carryall with a structured everyday profile.", "Accessories", 148.00m, id: DemoProductIds.ArcLeatherTote));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class DemoProductIds
{
    public static readonly Guid SolsticeLinenShirt = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MoriCeramicSet = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid FieldNotes = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ArcLeatherTote = Guid.Parse("44444444-4444-4444-4444-444444444444");
}