# Location-Based Platform (Leaflet + .NET + Next.js + IdentityServer)

A full-stack location discovery system with:

- 🗺️ Leaflet maps (Next.js frontend)
- 🛰️ .NET 8 Minimal API + PostGIS for geospatial search
- 🔐 Duende IdentityServer for authentication and access control IdentityServer: https://github.com/fady17/rally.git




> 🔗 **Frontend repo:** [github.com/fady17/location-platform-frontend](https://github.com/fady17/web.git )

---

## 🧭 Features

- Fast geospatial queries using PostGIS
- Secure login with OAuth2 / OpenID Connect via IdentityServer
- Protected APIs with access tokens
- Full-stack HTTPS setup for local development

---


---

## 🛠 Stack Overview

| Layer        | Tech                         |
|--------------|------------------------------|
| Frontend     | Next.js (with Bun) + Leaflet |
| API          | .NET 8 Minimal API + PostGIS |
| Auth         | Duende IdentityServer        |
| Identity     | NextAuth.js (OAuth client)   |

---

## 🔐 Auth Flow (Simplified)

1. Next.js uses **NextAuth.js** to initiate login with IdentityServer.
2. User authenticates → receives access & ID tokens.
3. Tokens are stored securely and sent with each API call.
4. The `.NET` backend validates tokens for protected endpoints.

---
Make sure PostgreSQL + PostGIS is running and appsettings.json contains the correct connection string.

dotnet run

🌐 Running the Frontend (with Bun)
	1.	Clone the frontend repo
	2.	Install Bun: