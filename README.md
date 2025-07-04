# Logistics API (v1)

This repository contains the .NET 7 backend service for the v1 Rally Logistics platform. It functions as a protected resource server, secured by the v1 Duende IdentityServer. The API is responsible for managing and serving all business and GIS-related data to authorized client applications.

## 1. Project Overview

The Logistics API is the central data service for the logistics platform. It provides a set of HTTP endpoints for clients to interact with core business entities such as operational areas, service depots (shops), and user-specific data like shopping carts and bookings.

The API is designed to support a feature-rich frontend by providing specialized, performant endpoints for map-based visualizations and user-driven queries.

### Key Responsibilities:

-   **JWT Token Validation:** Verifies JWT access tokens issued by the central Identity Provider to secure its endpoints.
-   **GIS Data Services:** Exposes endpoints that return geographic data, including service zones and depot locations, with support for spatial queries.
-   **Business Logic Management:** Handles the creation, retrieval, and modification of core entities like depots, services, user carts, and bookings.
-   **Anonymous & Authenticated User Support:** Provides distinct data management capabilities for both guest (anonymous) and registered users.
-   **System Maintenance Operations:** Includes secure administrative endpoints for triggering background data processing tasks.

## 2. System Architecture

This API is a component within a larger system. It expects to be called by client applications that have first authenticated users against the central Identity Provider.

-   **Authentication:** The API does not handle user login. It validates a `Bearer` token provided in the `Authorization` header of an incoming request. This validation checks the token's signature, issuer, and audience against values configured to trust the v1 Duende IdentityServer.
-   **Data Model:** The data architecture is built on a hierarchical GIS model. Official government boundaries (`AdministrativeBoundary`) form a base layer, upon which business-defined `OperationalArea` entities are built. This allows for flexible service zone definitions that can either match official boundaries or be composed of custom geographic shapes.
-   **Performance:** To ensure fast response times for read-heavy operations, the API makes extensive use of pre-joined database views (e.g., `ShopDetailsView`) and pre-calculated statistics (e.g., `AdminAreaShopStats`). Expensive calculations are handled by a background `IHostedService` to avoid impacting user request times.

## 3. Key Features

### GIS & Map Data Endpoints

-   **Dynamic Map Data:** The `/api/mapdata` endpoint provides data optimized for map UIs. It returns aggregated depot counts at low zoom levels and individual depot points at high zoom levels to ensure high performance.
-   **Spatial Querying:** Supports querying for depots within a geographic bounding box (the user's map view) and within a specified radius of a user's location.
-   **Distance-Based Sorting:** Search results can be sorted by distance from the user's current coordinates.
-   **Hierarchical Drill-Down:** API routes are structured logically (e.g., `/api/operational-areas/{areaSlug}/categories/{subCategorySlug}/shops`), allowing clients to navigate from a wide geographic area down to a specific set of depots.

### Anonymous & Registered User Features

-   **Anonymous Session Support:** A dedicated set of `/api/anonymous` endpoints issue and validate a custom JWT (`X-Anonymous-Token`) to manage state for guest users, including shopping carts and location preferences.
-   **Authenticated User Endpoints:** A separate group of `/api/users/me` endpoints are protected by OIDC token authentication and manage data for registered users.
-   **Data Merge on Registration:** A `/merge-anonymous-data` endpoint provides the crucial business logic to transfer a guest's cart and preferences to their account upon successful sign-up.

### System Operations

-   **Background Job Service:** An `IHostedService` (`ShopCountAggregationService`) runs on a timer to periodically update GIS-related statistics, ensuring data read by map endpoints is pre-calculated and readily available.
-   **Secure Admin Endpoint:** A dedicated administrative endpoint (`/api/admin/tasks/aggregate-shop-counts`) allows for manual triggering of background jobs. Access is protected by a custom filter requiring a secret API key (`X-Admin-Trigger-Key`).

## 4. API Endpoints

A high-level overview of the main endpoint groups. For full details, including request/response models, run the project and navigate to the Swagger UI.

-   **`GET /`**: Serves the interactive Swagger UI documentation.
-   **`GET /api/operational-areas-for-map`**: Returns geographic boundaries of service zones.
-   **`GET /api/operational-areas/{areaSlug}/...`**: Endpoints for querying data within a specific geographic context.
-   **`POST /api/anonymous/sessions`**: Creates a session for a guest user.
-   **`/api/anonymous/cart/...`**: Full CRUD endpoints for managing a guest's shopping cart.
-   **`/api/users/me/cart/...`**: **(Protected)** CRUD endpoints for managing a registered user's shopping cart.
-   **`/api/admin/tasks/...`**: **(Protected by Admin Key)** Endpoints for system maintenance tasks.

## 5. Development Setup

1.  **Prerequisites:** Ensure the **v1 Duende IdentityServer** is running.
2.  **Configuration:**
    -   In `appsettings.Development.json` or user secrets, configure the `DefaultConnection` string.
    -   Set `Authentication:Schemes:Bearer:Authority` to the URL of your running IdP.
    -   Set `Authentication:Schemes:Bearer:Audience` to the API's designated audience name (e.g., `urn:automotiveservicesapi`).
    -   Set the `AnonymousSession:JwtSecretKey`.
3.  **Database:** Run `dotnet ef database update` to apply migrations. The seeder will populate the database on the first run.
4.  **Run:** Use `dotnet run` to start the API.
