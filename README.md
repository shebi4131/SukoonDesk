# SukoonDesk

Every concern, resolved.

SukoonDesk is a multi-tenant complaint-management application for housing societies. It includes the public website, society and resident registration, approval workflows, complaint operations, automatic and manual assignment, RBAC, settings, reports, feedback, and in-app notifications.

## Included modules

- Multi-tenant society provisioning and isolated tenant data
- Society administrator signup, resident registration, approval, suspension, and role assignment
- Complaint queue, search, filtering, priority, SLA due dates, comments, internal notes, and audit history
- Configurable complaint categories and automatic assignment rules
- Controlled status transitions from submission through resolution, closure, and reopening
- Resident feedback and satisfaction reporting
- Dashboard metrics, six-month trends, SLA reporting, category distribution, and status distribution
- In-app notification inbox with unread state
- Tenant settings plus role, permission, category, routing, and membership CRUD

## Stack

- ASP.NET Core 10 minimal API
- ASP.NET Core Identity with HTTP-only cookies
- Entity Framework Core 10
- SQLite for zero-setup local development
- PostgreSQL/Npgsql for production
- React 19, TypeScript, Vite
- React Router and TanStack Query
- React Hook Form and Zod
- Lucide icons and custom accessible design tokens

## Run locally

Requirements: .NET 10 SDK and Node.js 24+.

```powershell
dotnet restore
npm install --prefix apps/web
dotnet run --project src/SukoonDesk.Api --launch-profile http
```

In a second terminal:

```powershell
npm run dev --prefix apps/web
```

Open `http://localhost:5173`. The first signup creates a tenant, administrator account, administrator and resident roles, permission mappings, starter settings, and default complaint categories.

SQLite is the default and creates `sukoondesk.db` inside the API project. No database installation is needed.

## Use PostgreSQL

```powershell
docker compose up -d postgres
$env:Database__Provider = "Postgres"
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=sukoondesk;Username=sukoondesk;Password=change-this-password"
dotnet run --project src/SukoonDesk.Api --launch-profile http
```

Use deployment secrets for production connection strings; do not commit them.

## Configuration precedence

1. `appsettings.json` contains infrastructure defaults and brand fallbacks.
2. Environment variables override deployment configuration.
3. `AppSettings` stores global or tenant-specific runtime settings.
4. Tenant public settings override public application defaults.

Common runtime keys include `society.name`, `complaints.defaultSlaHours`, `registration.residentApproval`, and `notifications.supportEmail`.

Email and WhatsApp delivery require provider credentials and a delivery adapter. The application currently provides the notification inbox and stores provider-related tenant settings without embedding any paid vendor dependency.

## RBAC model

Permissions are code-defined capabilities. A tenant administrator can create, edit, deactivate, and delete custom roles, then assign supported permissions. System roles cannot be deleted. Authorization is evaluated server-side using the current user, tenant membership, role, and permission mapping on every protected request.

The permission catalog lives in `Security/PermissionAuthorization.cs`; seed metadata is in `Infrastructure/SeedData.cs`.

## Production notes

The application now includes production-oriented defaults:

- PostgreSQL EF Core migrations in `Infrastructure/Migrations`; production startup applies pending migrations.
- Global CSRF validation for every mutating `/api` request and automatic React token handling.
- Global, authentication, and public endpoint rate limits.
- HSTS, HTTPS redirection, forwarded-header support, Problem Details, CSP and browser security headers.
- Database-backed ASP.NET Data Protection keys so sessions survive stateless service restarts.
- R2/S3-compatible evidence storage with local storage retained for development.
- Brevo and Resend HTTPS email providers, plus SMTP/pickup delivery for local development.
- Image size, MIME type and file-signature validation.
- Readiness and liveness endpoints.
- A multi-stage production Docker image that serves React and the API from one origin.
- Render Blueprint and GitHub Actions build workflow.
- Generated staff credentials require a password replacement before portal use.
- A separately authorized SukoonDesk platform administration dashboard.

### Local platform administrator

Development startup seeds a platform administrator only from `appsettings.Development.json`:

```text
Society URL: sukoondesk-admin
Email: admin@sukoondesk.local
Password: Sukoon@123
```

Sign in at `/sign-in`, then open `/app/platform`. Use `/app/profile` (or **Administrator profile** on the platform dashboard) to change the email and password. The database `IsPlatformAdmin` flag survives an email change. The known development password is explicitly rejected if platform seeding is enabled in Production.

For a production bootstrap account, set `PlatformAdmin__Seed__Enabled=true`, plus `PlatformAdmin__Seed__Email`, `PlatformAdmin__Seed__Password`, and optionally `PlatformAdmin__Seed__SocietySlug` as deployment secrets for the first start, then disable seeding. Alternatively, promote a known account through a controlled database operation or include its email under `PlatformAdmin__Emails`.

### Render + Neon + R2 + Brevo

1. Create a Neon PostgreSQL database.
2. Create a private Cloudflare R2 bucket and S3 API credentials.
3. Create a Brevo transactional-email API key and verify the sender.
4. Deploy this repository's `main` branch using `render.yaml`.
5. Supply every `sync: false` environment value in Render. Never commit those values.

The Docker image serves the Vite build and API from the same hostname, preserving secure cookie authentication without cross-site cookie configuration.

### Still required before handling sensitive production data

- Add and exercise automated API integration, authorization, tenant-isolation and browser end-to-end tests.
- Add PostgreSQL row-level-security policies as a defense-in-depth tenant boundary.
- Encrypt or tokenize CNIC values at the application layer and define access/retention policy.
- Protect Data Protection key XML with a production certificate or managed key service.
- Configure centralized logs, error alerts, uptime monitoring, database backups and restore drills.
- Complete an independent security review and penetration test.
- Replace emailed temporary passwords with expiring set-password links when the onboarding workflow is finalized.
- Add background jobs/retries for email and future WhatsApp delivery.

## Branch workflow

- `shoaib-dev`: day-to-day implementation.
- `staging`: integration and acceptance testing.
- `main`: production releases only.

Promote changes with pull requests: `shoaib-dev` → `staging` → `main`.
