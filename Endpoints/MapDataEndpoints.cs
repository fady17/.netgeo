// File: Endpoints/MapDataEndpoints.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
// using AutomotiveServices.Api.Models; // Not strictly needed if ShopDetailsView projects category as string
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; // For [AsParameters] and ProblemDetails
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries; // For Coordinate, Polygon, LinearRing, GeometryFactory
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Endpoints
{
    /// <summary>
    /// Endpoints related to fetching data for map display.
    /// </summary>
    public static class MapDataEndpoints
    {
        // Static GeometryFactory instance for creating geometries. SRID 4326 is for WGS84 (standard lat/lon).
        private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);

        /// <summary>
        /// Maps the /api/mapdata endpoint.
        /// </summary>
        /// <param name="app">The IEndpointRouteBuilder to add the route to.</param>
        public static void MapMapDataEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/mapdata", HandleMapDataRequest)
                .WithName("GetMapData")
                .WithTags("Map Data")
                .WithSummary("Provides geographic features (aggregates or points) for map display based on viewport and zoom.")
                .Produces<List<MapFeatureDtoBase>>(StatusCodes.Status200OK) // Base type for Swagger, actual list will contain derived types
                .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
                .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
                .AllowAnonymous(); // This endpoint is publicly accessible
        }

        /// <summary>
        /// Handles requests to the /api/mapdata endpoint.
        /// Determines whether to return aggregated administrative area data or individual shop points
        /// based on the provided zoom level and bounding box.
        /// </summary>
        private static async Task<IResult> HandleMapDataRequest(
            [AsParameters] MapDataRequestParameters queryParams, // DTO for query parameters
            AppDbContext dbContext,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger("MapDataEndpoints");
            logger.LogInformation(
                "Received map data request: MinLat={MinLat}, MinLon={MinLon}, MaxLat={MaxLat}, MaxLon={MaxLon}, Zoom={ZoomLevel}",
                queryParams.MinLat, queryParams.MinLon, queryParams.MaxLat, queryParams.MaxLon, queryParams.ZoomLevel);

            // Validate bounding box parameters to ensure they form a valid rectangle
            if (queryParams.MinLat >= queryParams.MaxLat || queryParams.MinLon >= queryParams.MaxLon ||
                queryParams.MinLat < -90 || queryParams.MaxLat > 90 || queryParams.MinLat > 90 || queryParams.MaxLat < -90 || // Check individual lat bounds
                queryParams.MinLon < -180 || queryParams.MaxLon > 180 || queryParams.MinLon > 180 || queryParams.MaxLon < -180) // Check individual lon bounds
            {
                logger.LogWarning("Invalid bounding box parameters received: MinLat={MinLat}, MinLon={MinLon}, MaxLat={MaxLat}, MaxLon={MaxLon}",
                    queryParams.MinLat, queryParams.MinLon, queryParams.MaxLat, queryParams.MaxLon);
                return Results.BadRequest(new ProblemDetails { Title = "Invalid Bounding Box", Detail = "Provided latitude/longitude values for bounding box are invalid or form an impossible rectangle.", Status = StatusCodes.Status400BadRequest });
            }
            
            // Read configuration thresholds from appsettings.json (or defaults)
            int zoomThreshold = configuration.GetValue<int>("MapSettings:ZoomThresholdForAdminAggregates", 9);
            int maxShopsToReturn = configuration.GetValue<int>("MapSettings:MaxIndividualShopsToReturn", 300);

            // Create a GEOGRAPHY polygon from the request's bounding box.
            // NTS and PostGIS expect coordinates in (longitude, latitude) order for polygons.
            var envelopeCoordinates = new Coordinate[] {
                new Coordinate(queryParams.MinLon, queryParams.MinLat), // Southwest corner
                new Coordinate(queryParams.MaxLon, queryParams.MinLat), // Southeast corner
                new Coordinate(queryParams.MaxLon, queryParams.MaxLat), // Northeast corner
                new Coordinate(queryParams.MinLon, queryParams.MaxLat), // Northwest corner
                new Coordinate(queryParams.MinLon, queryParams.MinLat)  // Close the ring by repeating the first point
            };
            var requestBoundingBox = _geometryFactory.CreatePolygon(new LinearRing(envelopeCoordinates));
            // SRID 4326 (WGS84) is assumed for GEOGRAPHY types interacting with PostGIS
            requestBoundingBox.SRID = 4326; 

            var featuresToReturn = new List<MapFeatureDtoBase>();
            string featureTypeReturned = "unknown"; // For logging

            try
            {
                if (queryParams.ZoomLevel < zoomThreshold)
                {
                    featureTypeReturned = "admin aggregates";
                    logger.LogInformation("Zoom level {ZoomLevel} is less than threshold {Threshold}. Fetching admin aggregates.", 
                        queryParams.ZoomLevel, zoomThreshold);
                    
                    // Fetch pre-aggregated shop counts for administrative areas (Governorates)
                    var adminAggregatesData = await dbContext.AdminAreaShopStats
                        .AsNoTracking()
                        .Include(s => s.AdministrativeBoundary) // Eagerly load related AdministrativeBoundary
                        .Where(s => s.AdministrativeBoundary.AdminLevel == 1 &&      // Only Governorates
                                     s.AdministrativeBoundary.IsActive &&
                                     s.AdministrativeBoundary.Boundary != null &&    // Ensure boundary exists for intersection
                                     s.AdministrativeBoundary.Boundary.Intersects(requestBoundingBox)) // Spatial query
                        .Select(s => new // Anonymous type to fetch necessary data including the NTS Point object
                        {
                            s.AdministrativeBoundaryId,
                            s.AdministrativeBoundary.NameAr,
                            s.AdministrativeBoundary.NameEn,
                            CentroidGeo = s.AdministrativeBoundary.Centroid, // The NTS Point object
                            s.ShopCount
                        })
                        .ToListAsync(); // Materialize data from DB

                    // Project to the final DTO in memory to access NTS Point properties
                    featuresToReturn.AddRange(adminAggregatesData.Select(s => new AdminAggregateMapFeatureDto
                    {
                        Id = s.AdministrativeBoundaryId,
                        NameAr = s.NameAr,
                        NameEn = s.NameEn,
                        CentroidLat = s.CentroidGeo?.Y ?? 0, // Access .Y (Latitude) from NTS Point, handle null
                        CentroidLon = s.CentroidGeo?.X ?? 0, // Access .X (Longitude) from NTS Point, handle null
                        ShopCount = s.ShopCount
                    }));
                }
                else // Zoom level is at or above the threshold for showing individual shops
                {
                    featureTypeReturned = "shop points";
                    logger.LogInformation("Zoom level {ZoomLevel} is GTE threshold {Threshold}. Fetching individual shops (max: {MaxShops}).", 
                        queryParams.ZoomLevel, zoomThreshold, maxShopsToReturn);

                    // Fetch individual shops, preferably from an optimized view like ShopDetailsView
                    // Ensure ShopDetailsView.Location is the actual GEOGRAPHY Point and is indexed.
                    featuresToReturn.AddRange(await dbContext.ShopDetailsView 
                        .AsNoTracking()
                        .Where(s => !s.IsDeleted && 
                                     s.Location != null && // Ensure shop location exists
                                     s.Location.Intersects(requestBoundingBox)) // Spatial query
                        .OrderBy(s => s.Id) // Consistent ordering can be useful for Take(), though not strictly required
                        .Take(maxShopsToReturn) // Limit the number of shops returned
                        .Select(s => new ShopPointMapFeatureDto
                        {
                            Id = s.Id,
                            NameAr = s.NameAr, // Ensure ShopDetailsView has NameAr
                            NameEn = s.NameEn,
                            Lat = s.ShopLatitude,  // Pre-calculated latitude from ShopDetailsView
                            Lon = s.ShopLongitude, // Pre-calculated longitude from ShopDetailsView
                            Category = s.Category.ToString(), // Convert ShopCategory enum to string
                            LogoUrl = s.LogoUrl
                        })
                        .ToListAsync());
                }

                // Detailed logging of features before sending (can be verbose, conditional for debug if needed)
                // foreach (var feature in featuresToReturn)
                // {
                //     if (feature is AdminAggregateMapFeatureDto aggFeature)
                //     {
                //         logger.LogDebug("Aggregate Feature to Serialize: Id={Id}, NameAr={NameAr}, Type={Type}",
                //             aggFeature.Id, aggFeature.NameAr, aggFeature.Type);
                //     }
                //     else if (feature is ShopPointMapFeatureDto shopFeature)
                //     {
                //         logger.LogDebug("Shop Feature to Serialize: Id={Id}, NameAr={NameAr}, Type={Type}",
                //             shopFeature.Id, shopFeature.NameAr, shopFeature.Type);
                //     }
                // }
            
                logger.LogInformation("Returning {Count} {FeatureType} features.", featuresToReturn.Count, featureTypeReturned); // Corrected log message
                return Results.Ok(featuresToReturn);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while fetching map data for BBOX:[{MinLon},{MinLat},{MaxLon},{MaxLat}] Zoom:{Zoom}",
                    queryParams.MinLon, queryParams.MinLat, queryParams.MaxLon, queryParams.MaxLat, queryParams.ZoomLevel);
                return Results.Problem("An unexpected error occurred while fetching map data.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }
    }
}