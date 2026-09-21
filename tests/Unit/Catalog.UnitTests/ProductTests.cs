using Catalog.Domain;

namespace Catalog.UnitTests;

public sealed class ProductTests
{
    [Fact]
    public void Create_normalizes_slug_and_currency()
    {
        var product = Product.Create(" Linen Shirt ", " Linen-Shirt ", "A shirt", "Apparel", 89m, "usd");

        Assert.Equal("linen-shirt", product.Slug);
        Assert.Equal("USD", product.Currency);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Create_rejects_negative_price()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create("Shirt", "shirt", "A shirt", "Apparel", -1m));
    }
}