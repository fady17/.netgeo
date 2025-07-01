# .netgeo
# Location-Based Platform (Leaflet + .NET + Next.js)

A minimal location discovery system built with:

- 🌍 Leaflet for map rendering (Next.js frontend)
- 🛰️ .NET (Minimal API) with PostGIS for native spatial queries
- 📦 REST API secured over HTTPS

> 🔗 **Frontend repo:** [github.com/fady17/location-platform-frontend](https://github.com/fady17/location-platform-frontend)

---

## 🧭 Features

- Fast geospatial queries using PostGIS
- Marker rendering via Leaflet
- Client–server separation with clean REST APIs
- Designed for local discovery and filtering

---

## 🛠 Tech Stack

- **Backend**: .NET 8 Minimal APIs + PostgreSQL + PostGIS
- **Frontend**: Next.js (Bun runtime), Leaflet, Tailwind
- **Spatial Indexing**: PostGIS `ST_DWithin`, `ST_Transform`

---

## 🧪 Running the Backend (HTTPS with Kestrel)

1. Create a self-signed dev certificate:

```bash
dotnet dev-certs https --trust