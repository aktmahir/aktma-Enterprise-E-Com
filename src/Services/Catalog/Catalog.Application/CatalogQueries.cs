using Catalog.Domain;
using MediatR;

namespace Catalog.Application;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> SearchAsync(string? search, string? category, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record GetProductsQuery(string? Search, string? Category, int Page = 1, int PageSize = 12) : IRequest<ProductListResponse>;

public sealed record ProductResponse(Guid Id, string Name, string Slug, string Description, string Category, decimal Price, string Currency)
{
    public static ProductResponse From(Product product) => new(product.Id, product.Name, product.Slug, product.Description, product.Category, product.Price, product.Currency);
}

public sealed record ProductListResponse(IReadOnlyList<ProductResponse> Items, int Page, int PageSize, int TotalCount);

public sealed class GetProductsQueryHandler(IProductRepository repository) : IRequestHandler<GetProductsQuery, ProductListResponse>
{
    public async Task<ProductListResponse> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var products = await repository.SearchAsync(request.Search, request.Category, page, pageSize, cancellationToken);
        var items = products.Select(ProductResponse.From).ToArray();
        return new ProductListResponse(items, page, pageSize, items.Length);
    }
}