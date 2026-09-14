# Deploy ProACC Web on Render

The Dockerfile builds .NET 10 for Linux and installs the matching Chromium browser for PDF exports. The /health endpoint checks process liveness only, not SQL connectivity or demo readiness.

## Required setup

- Create a SQL Server database reachable from Render, with the existing ProACC schema and synthetic demonstration data. Render Postgres is not compatible with the SQL Server provider used by this app.
- Use SQL authentication and encrypted connections, not Windows Integrated Security or LocalDB. Enter secrets only in Render environment settings.
- Set ConnectionStrings__DefaultConnection and ConnectionStrings__CustomUserConnection to the appropriate SQL Server connections.
- Set SystemAdmin__PasswordHash to the SHA-256 hex digest of a private administrator password.
- Set CompanyDatabases__Databases__0__DatabaseName to the demonstration database name, and CompanyDatabases__Databases__0__LoginCodeHash to the SHA-256 hex digest of the demonstration company code (at least eight characters). Configure a test user in that company database.

## Render service

Use a Blueprint from render.yaml, or create a Docker Web Service from this repository and the main branch. Leave the root directory empty, use ./Dockerfile, and set the health check to /health. The process listens on Render's PORT. ASPNETCORE_FORWARDEDHEADERS_ENABLED=true is intended only for the Render service behind its managed proxy.

The included Blueprint selects the free plan and does not provision a database or paid storage. Before sharing the demo, verify company selection, login, an accounting page, and PDF export with synthetic data.

## Persistence

Free instances lose filesystem changes on restarts/redeployments. Company registry edits, encryption keys, and audit files will not persist. Journal attachments are now stored on the user device and are unaffected by Render restarts. The registry is seeded from environment configuration when its file is missing; configure all demo companies there when using the free plan.

For persistent use, choose a paid service with a disk mounted at /app/App_Data and verify the app user can write to it. The company AttachPath setting describes a folder on the user device, for example C:\Attach. It is not a server filesystem path. Paid service/disk selection requires a separate spending decision.

## Configuration

appsettings.Local.json is loaded only in Development. Production settings must use Render environment variables. Do not commit local settings, database backups, real company data, passwords, or encryption keys.

## Local journal attachments

Use Chrome or Edge on a desktop over HTTPS. In the entry editor, open local attachments, choose the folder specified by AttachPath, and grant read/write permission. Files are copied directly in the browser; neither file contents nor filenames are sent to the application server. The server only checks access and returns company/entry metadata.

Each dialog asks for a folder. Browser APIs cannot verify or automatically open the full configured path. Subfolders use a stable company identifier and a year-entry identifier. Existing files in older server folders are not migrated or deleted. Each saved file has a unique suffix to avoid overwriting. Users on other devices cannot see these files unless they have their own copy of the same folders.

Validation: `node --test tests/local-journal-attachments.test.mjs` covers folder isolation, traversal, limits, unique names, and a mocked browser save flow. A real folder permission prompt must be accepted by the user.