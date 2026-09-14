FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
# Razor files must exist during restore so .NET includes the Blazor web assets.
COPY . .
RUN dotnet restore BlazorApp1/BlazorApp1.csproj -r linux-x64
COPY . .
RUN dotnet publish BlazorApp1/BlazorApp1.csproj -c Release -r linux-x64 --no-restore --self-contained false -o /out /p:UseAppHost=false
RUN test -s /out/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /out .
RUN chmod +x .playwright/node/linux-x64/node \
    && .playwright/node/linux-x64/node .playwright/package/cli.js install --with-deps chromium \
    && mkdir -p /app/App_Data \
    && chown -R app:app /app/App_Data \
    && chmod -R a+rX /ms-playwright \
    && rm -rf /var/lib/apt/lists/*
USER app
EXPOSE 10000
CMD ["sh", "-c", "exec dotnet BlazorApp1.dll --urls http://0.0.0.0:${PORT:-10000}"]
