# Command Cheatsheet

| Command | Working Directory | Status | Use |
| --- | --- | --- | --- |
| `rg --files -g '!frontend/node_modules' -g '!frontend/dist'` | repo root | Verified | Inventory source files quickly. |
| `git status --short -- .` | repo root | Verified | Check local state. Current output is `?? ./`. |
| `git rev-parse HEAD` | repo root | Verified failure | Shows there is no pinned commit yet. |
| `dotnet --version` | repo root | Verified failure | Confirms .NET SDK missing locally. |
| `docker compose up -d` | repo root | Inferred | Start PostgreSQL and Redis for local backend. |
| `dotnet run --project src/NetAuth0.Api` | repo root | Not verified | Start API after installing .NET 8. |
| `dotnet test tests/NetAuth0.UnitTests/NetAuth0.UnitTests.csproj` | repo root | Not verified | Run unit tests after installing .NET 8. |
| `npm install` | `frontend` | Verified | Install frontend dependencies. |
| `npm audit --audit-level=moderate` | `frontend` | Verified | Check dependency advisories. |
| `npm run build` | `frontend` | Verified | Typecheck and build React app. |

CI runs backend restore/build/test and frontend install/build in [`ci.yml`](../../../.github/workflows/ci.yml#L8-L32).
