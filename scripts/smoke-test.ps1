$ErrorActionPreference = 'Stop'

$checks = @(
    @{ Name = 'Gateway'; Url = 'http://localhost:5101/health' },
    @{ Name = 'Catalog'; Url = 'http://localhost:5013/health' },
    @{ Name = 'Identity'; Url = 'http://localhost:5129/health' },
    @{ Name = 'Inventory'; Url = 'http://localhost:5130/health' },
    @{ Name = 'Orders'; Url = 'http://localhost:5213/health' },
    @{ Name = 'Payments'; Url = 'http://localhost:5153/health' },
    @{ Name = 'Notifications'; Url = 'http://localhost:5179/health' },
    @{ Name = 'Cart'; Url = 'http://localhost:5242/health' }
)

foreach ($check in $checks) {
    $response = Invoke-RestMethod -Uri $check.Url
    if ($response.status -ne 'healthy') { throw "$($check.Name) is not healthy." }
    Write-Host "[OK] $($check.Name)"
}

$catalog = Invoke-RestMethod -Uri 'http://localhost:5101/api/v1/catalog/products?pageSize=1'
if ($catalog.totalCount -lt 1) { throw 'Catalog returned no seeded products.' }
Write-Host '[OK] Seeded catalog'

$customerId = [guid]::NewGuid()
$product = $catalog.items[0]
$body = @{ customerId = $customerId; currency = 'USD'; items = @(@{ productId = $product.id; name = $product.name; quantity = 1; unitPrice = $product.price }) } | ConvertTo-Json -Depth 5
$order = Invoke-RestMethod -Method Post -Uri 'http://localhost:5101/api/v1/orders' -ContentType 'application/json' -Body $body

for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Milliseconds 500
    $status = (Invoke-RestMethod -Uri "http://localhost:5101/api/v1/orders/$($order.id)").status
    if ($status -eq 'Confirmed') { Write-Host '[OK] Main order workflow'; Write-Host 'Local demo is ready.'; exit 0 }
    if ($status -eq 'Failed') { throw 'Main order workflow failed.' }
}

throw 'Main order workflow did not reach a final state in time.'