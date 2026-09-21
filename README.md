# Enterprise E-Commerce Platform

This repository is being built as a .NET 10 microservices e-commerce platform with a React and TypeScript storefront. The first runnable vertical slice is the Catalog bounded context: a DDD product model, MediatR query handler, HTTP API, seeded repository, frontend search and category filtering, Docker development dependencies, and unit tests.

## Current Status

Implemented and verified:

- Catalog API: `GET /api/v1/products`, search, category filtering, pagination bounds, and `/health`
- Catalog domain/application/infrastructure project boundaries
- MediatR-based query dispatch
- Responsive React storefront with loading, empty, and error states
- PostgreSQL, Redis, and RabbitMQ local dependencies in Docker Compose
- Catalog domain unit tests

Identity, Inventory, Cart, Orders, Payments, Notifications, and Gateway now have runnable local API slices. Their next hardening step is replacing their process-local stores with database-per-service persistence and adding hosted RabbitMQ consumers for the published contracts.

## Run Locally

Prerequisites: .NET SDK 10, Node.js 22+, npm, and Docker Desktop.

```powershell
dotnet run --project src/Services/Catalog/Catalog.Api --launch-profile http
Push-Location frontend/ecommerce-web
npm install
npm run dev
Pop-Location
```

The API is available at `http://localhost:5013`; the storefront is available at `http://localhost:5173`. For local dependencies, run `docker compose up --build`.

## Test

```powershell
dotnet test EnterpriseECommerce.slnx
Push-Location frontend/ecommerce-web
npm run build
Pop-Location
```

## Architecture Direction

Each future service owns its data and publishes integration events through RabbitMQ. Orders will coordinate inventory and payment through an eventual-consistency workflow with idempotent consumers, an outbox, retries, and compensating actions. Redis will hold cart state and selected read caches. PostgreSQL remains database-per-service.

See [docs/architecture.md](docs/architecture.md) for the current boundary map and delivery order.