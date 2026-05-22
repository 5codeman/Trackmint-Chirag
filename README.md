# TrackMint

TrackMint is a full-stack personal finance tracker built with React, TypeScript, ASP.NET Core, PostgreSQL, RabbitMQ, Docker, JWT authentication, and GitHub Actions. The project is structured as a microservices-focused system for interview-ready architecture discussions.

## Features

- JWT authentication with refresh token flow
- Account and wallet management
- Transaction CRUD with filtering and transfer support
- Category management
- Monthly budgets and utilization tracking
- Savings goals with contribution and withdrawal flows
- Recurring transactions with background processing
- Shared accounts with owner/editor/viewer roles
- Dashboard summary and reporting charts
- Cash-flow forecasting
- Financial health score and insight cards
- Automation rules engine for transaction processing
- CSV export
- Notification service for event-driven user notifications

## Tech Stack

| Layer | Technology |
| --- | --- |
| Frontend | React, TypeScript, Vite, React Query, Zustand, Recharts |
| Backend | ASP.NET Core 10 |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| Messaging | RabbitMQ |
| Auth | JWT, refresh tokens, PBKDF2 password hashing |
| Architecture | API Gateway, microservices, event-driven communication |
| DevOps | Docker Compose, GitHub Actions |
| Testing | xUnit service-level tests |

## Microservices Architecture

```text
React Frontend
    |
    v
API Gateway
    |
    |-- Auth Service
    |-- Finance Service
    |-- Insights Service
    |-- Notification Service
    |
    v
RabbitMQ
```

## Services

| Service | Responsibility |
| --- | --- |
| API Gateway | Single backend entry point, request routing, correlation ID forwarding, rate limiting |
| Auth Service | Register, login, refresh token, forgot/reset password, JWT issuing |
| Finance Service | Accounts, categories, transactions, budgets, goals, recurring transactions, rules |
| Insights Service | Dashboard, reports, forecast, financial health score, insight cards |
| Notification Service | Stores notifications and consumes RabbitMQ events |
| Contracts | Shared integration event contracts |

## RabbitMQ Event Flow

TrackMint uses RabbitMQ for asynchronous service-to-service communication.

```text
Finance Service publishes event
        |
        v
RabbitMQ exchange: trackmint.events
        |
        v
Notification Service consumes event
        |
        v
Notification stored in PostgreSQL
```

Events include:

- `auth.user.registered`
- `finance.transaction.created`
- `finance.transaction.updated`
- `finance.transaction.deleted`
- `finance.budget.threshold_crossed`
- `finance.goal.completed`
- `finance.recurring.generated`

RabbitMQ keeps services loosely coupled. Finance does not directly call Notification; it publishes events and Notification reacts independently.

## Repository Layout

```text
.
|- backend/
|  |- PersonalFinanceTracker.Api
|  |- PersonalFinanceTracker.Application
|  |- PersonalFinanceTracker.Domain
|  `- PersonalFinanceTracker.Infrastructure
|- frontend/
|- services/
|  |- TrackMint.Gateway
|  |- TrackMint.AuthService
|  |- TrackMint.FinanceService
|  |- TrackMint.InsightsService
|  |- TrackMint.NotificationService
|  `- TrackMint.Contracts
|- tests/
|  |- TrackMint.AuthService.Tests
|  |- TrackMint.Contracts.Tests
|  `- TrackMint.NotificationService.Tests
|- compose.yaml
`- compose.microservices.yaml
```

## Run Microservices With Docker Compose

Start the microservices stack:

```bash
docker compose -f compose.microservices.yaml up --build
```

Services:

```text
API Gateway:          http://localhost:8090
Auth Service:         http://localhost:5101
Finance Service:      http://localhost:5102
Insights Service:     http://localhost:5103
Notification Service: http://localhost:5104
RabbitMQ UI:          http://localhost:15672
PostgreSQL:           localhost:5434
```

RabbitMQ credentials:

```text
Username: guest
Password: guest
```

If an old database volume exists and service databases are not created, reset the stack:

```bash
docker compose -f compose.microservices.yaml down -v
docker compose -f compose.microservices.yaml up --build
```

## Smoke Test

After the microservices stack is running:

```powershell
.\scripts\smoke-microservices.ps1
```

The smoke test checks:

- Gateway health
- Service routing through the gateway
- User registration through Auth Service
- JWT-authenticated Notification Service access
- Welcome notification flow through RabbitMQ

## Frontend Setup

Install dependencies:

```bash
cd frontend
npm install
```

For microservices mode, create an environment file using:

```env
VITE_API_BASE_URL=http://localhost:8090/api
```

Run frontend:

```bash
npm run dev
```

Build frontend:

```bash
npm run build
```

## Monolith Mode

The original ASP.NET Core backend is still available for comparison and migration safety.

Run monolith stack:

```bash
docker compose up --build
```

Swagger for the original backend:

```text
http://localhost:8080/swagger
```

## Tests

Run service-level tests:

```bash
dotnet test tests/TrackMint.AuthService.Tests/TrackMint.AuthService.Tests.csproj --configuration Release
dotnet test tests/TrackMint.Contracts.Tests/TrackMint.Contracts.Tests.csproj --configuration Release
dotnet test tests/TrackMint.NotificationService.Tests/TrackMint.NotificationService.Tests.csproj --configuration Release
```

Test coverage currently focuses on:

- Password hashing behavior
- JWT claim generation
- Integration event contract stability
- Notification event mapping

## CI

GitHub Actions validates the microservices branch by running:

- .NET restore
- Service builds
- Original backend build
- xUnit tests
- Docker Compose config validation
- Docker image builds

## Interview Highlights

This project demonstrates:

- Microservices architecture with clear service boundaries
- API Gateway pattern
- JWT authentication across services
- PostgreSQL database ownership per service direction
- RabbitMQ-based event-driven communication
- Async notification flow
- Background worker processing
- Docker Compose orchestration
- CI pipeline for build, tests, compose validation, and image builds
- Unit testing around security, contracts, and event mapping

## Production Improvements

Future production hardening can include:

- Transactional outbox pattern for guaranteed event publishing
- Centralized logging and distributed tracing
- Metrics and dashboards
- API Gateway JWT validation
- Separate production secrets management
- Kubernetes deployment
- Deployment pipeline for cloud environments
