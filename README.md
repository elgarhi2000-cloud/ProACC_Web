# ProACC Web

ASP.NET Core Blazor accounting application targeting .NET 10 with SQL Server.

## Run locally

1. Install the .NET 10 SDK and configure SQL Server.
2. Create `BlazorApp1/appsettings.Local.json` with your environment-specific connection strings and company database configuration. This file is excluded from Git.
3. Run `dotnet restore BlazorApp1.slnx`.
4. Run `dotnet run --project BlazorApp1/BlazorApp1.csproj`.

Local database settings, company login hashes, encryption keys, runtime data, and build outputs are intentionally excluded. Configure the required databases and credentials before using the application.
