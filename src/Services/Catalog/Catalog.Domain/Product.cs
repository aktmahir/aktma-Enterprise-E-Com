namespace Catalog.Domain;

public sealed class Product
{
    private Product() { }

    private Product(Guid id, string name, string slug, string description, string category, decimal price, string currency, bool isActive)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Description = description;
        Category = category;
        Price = price;
        Currency = currency;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public static Product Create(string name, string slug, string description, string category, decimal price, string currency = "USD", Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Product slug is required.", nameof(slug));
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));

        return new Product(id ?? Guid.NewGuid(), name.Trim(), slug.Trim().ToLowerInvariant(), description.Trim(), category.Trim(), price, currency.Trim().ToUpperInvariant(), true);
    }
}