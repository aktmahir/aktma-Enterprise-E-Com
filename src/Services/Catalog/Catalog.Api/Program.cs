using Catalog.Application;
using Catalog.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<GetProductsQuery>());
builder.Services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));
builder.Services.AddScoped<IProductRepository, EfProductRepository>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await CatalogSeedData.InitializeAsync(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "catalog" }));
app.MapGet("/api/v1/products", async (string? search, string? category, int? page, int? pageSize, ISender sender, CancellationToken cancellationToken) =>
{
    var result = await sender.Send(new GetProductsQuery(search, category, page ?? 1, pageSize ?? 12), cancellationToken);
    return Results.Ok(result);
}).WithName("GetProducts");

app.Run();

public partial class Program;
