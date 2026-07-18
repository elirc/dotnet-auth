PostgreSQL runs from the root `docker-compose.yml`.

The API currently falls back to `EnsureCreated` when no compiled migrations are present. After installing the .NET SDK, generate the first migration with:

```powershell
dotnet ef migrations add InitialIdentitySchema --project src/NetAuth0.Infrastructure --startup-project src/NetAuth0.Api --output-dir Persistence/Migrations
```
