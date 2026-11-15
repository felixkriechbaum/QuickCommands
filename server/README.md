# QuickCommands Server

ASP.NET Core 10 API for **Cloud Save** and the **Store** of QuickCommands. Design and all decisions:
[docs/DESIGN.md](docs/DESIGN.md). API description: [openapi/v1.json](openapi/v1.json) (generated on build).

## Develop

```bash
docker compose -f compose.dev.yml up -d            # MySQL 8.4 on localhost:3307
dotnet run --project src/QuickCommands.Server       # http://localhost:5080, migrations run on startup
open http://localhost:5080/scalar                   # API reference (Development only)
dotnet test QuickCommands.Server.slnx               # needs Docker: integration tests start their own MySQL
```

Signed-in endpoints need a Firebase ID token of the project in `appsettings.Development.json` (`Firebase:ProjectId`).

New migration after changing entities or configurations:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/QuickCommands.Server --output-dir Data/Migrations
```

`dotnet build` regenerates `openapi/v1.json` – commit it together with API changes (CI fails otherwise).

## Deploy

CI (`.github/workflows/server.yml`) pushes `ghcr.io/felixkriechbaum/quickcommands-server:<YYYY.M.build>` and
`:latest` for every green push to `main` that touches `server/`.

1. **MySQL:** create the database and a user that only has rights on it (DDL included – migrations run on startup):
   ```sql
   CREATE DATABASE quickcommands CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
   CREATE USER 'quickcommands'@'%' IDENTIFIED BY '…';
   GRANT ALL PRIVILEGES ON quickcommands.* TO 'quickcommands'@'%';
   ```
2. **loggerui:** create the project `quickcommands`, generate its upload token, set the alert to level `warn`,
   mode `all` and your webhook (Discord / ntfy). Reports on store packages arrive through this alert.
3. **Stack:** copy `compose.example.yml` and `.env.example` (as `.env`) to the server, fill in the values,
   `docker compose -f compose.example.yml up -d`.
4. **nginx** on the host:
   ```nginx
   location / {
       proxy_pass http://127.0.0.1:3005;
       proxy_set_header Host $host;
       proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
       proxy_set_header X-Forwarded-Proto $scheme;
       client_max_body_size 2m;
   }
   ```
5. Check: `curl https://<domain>/api/health` → `{"status":"ok","version":"…","database":"ok"}`.
