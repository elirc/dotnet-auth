# NetAuth0

Standalone identity provider for learning OAuth2/OIDC, JWT signing, refresh-token rotation, RBAC, MFA, and audit logging.

## Stack

- .NET 8 Web API, layered as API -> Application -> Domain -> Infrastructure
- EF Core + PostgreSQL
- React + TypeScript + Vite
- TanStack Query, react-hook-form, zod
- BCrypt password hashing, TOTP MFA, RS256 JWTs, JWKS, key rotation

## Run Locally

1. Install .NET 8 SDK, Node 22+, and Docker.
2. Start infrastructure:

```powershell
docker compose up -d
```

3. Start the API:

```powershell
dotnet run --project src/NetAuth0.Api
```

4. Start the frontend:

```powershell
cd frontend
npm install
npm run dev
```

The API uses `http://localhost:5080` as its issuer by default. The frontend runs at `http://localhost:5173`.

Seeded admin login:

- Tenant slug: `default`
- Email: `admin@netauth0.local`
- Password: `ChangeMe123!`

## Implemented Features

- Tenant, client application, user, role, permission, authorization code, refresh token, signing key, audit, and MFA data model.
- OAuth/OIDC discovery at `/.well-known/openid-configuration`.
- JWKS at `/.well-known/jwks.json`.
- Authorization Code + PKCE, Client Credentials, and Refresh Token grant handling.
- Exact redirect URI matching, one-time short-lived auth codes, PKCE verification, and refresh-token rotation.
- Refresh-token replay detection with family revocation.
- RS256 access and ID tokens with permissions embedded in access tokens.
- Signing key rotation endpoint that keeps previous keys available through JWKS.
- Signup, login, brute-force lockout, TOTP enrollment/verification, recovery code generation, sessions, and session family revoke.
- Admin APIs for applications, users, role assignment, force logout, audit search, and key rotation.
- React admin dashboard for applications, users, audit logs, and basic analytics.
- Docker Compose for PostgreSQL and Redis.
- GitHub Actions CI for backend build/tests and frontend build.

## Useful Endpoints

- `POST /connect/token`
- `GET /connect/authorize`
- `GET /.well-known/openid-configuration`
- `GET /.well-known/jwks.json`
- `POST /api/v1/auth/signup`
- `POST /api/v1/auth/login`
- `GET /api/v1/admin/applications`
- `GET /api/v1/admin/users`
- `GET /api/v1/admin/audit-events`
- `POST /api/v1/admin/signing-keys/rotate`

## Example Client Registration

Use the dashboard Applications page, or call:

```http
POST /api/v1/admin/applications
Authorization: Bearer <admin access token>
Content-Type: application/json

{
  "tenantId": "<tenant-guid>",
  "name": "NetLinear",
  "type": "WebApp",
  "redirectUris": ["http://localhost:5174/callback"],
  "grantTypes": ["AuthorizationCode", "RefreshToken"],
  "requireConsent": true,
  "refreshTokenLifetimeDays": 30
}
```

## Notes

The current machine did not have the .NET SDK installed when this scaffold was created, so EF migration files were not generated locally. The infrastructure project includes a design-time `AppDbContext` factory; run the migration command in `infra/postgres/README.md` after installing .NET.
