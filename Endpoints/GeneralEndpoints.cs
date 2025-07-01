// src/AutomotiveServices.Api/Endpoints/GeneralEndpoints.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models; 
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;     
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using System.Collections.Generic;   
using NetTopologySuite.IO;          

namespace AutomotiveServices.Api.Endpoints
{
    public static class GeneralEndpoints
    {
        public static void MapGeneralEndpoints(this IEndpointRouteBuilder app)
        {
            // var group = app.MapGroup("/api/general").WithTags("General"); // Changed group to /api/general for clarity
            var group = app.MapGroup("/api").WithTags("General");
            group.MapGet("/operational-areas-for-map", 
                async (AppDbContext dbContext, 
                       ILoggerFactory loggerFactory, // Use ILoggerFactory
                       [FromQuery] string? displayLevel) => // <<< NEW PARAMETER
            {
                var logger = loggerFactory.CreateLogger("GeneralEndpoints.GetOperationalAreasForMap");
                logger.LogInformation("Fetching active operational areas for map display. Optional DisplayLevel filter: {DisplayLevel}", displayLevel);
                
                var geoJsonWriter = new GeoJsonWriter();

                var query = dbContext.OperationalAreas
                    .AsNoTracking()
                    .Include(oa => oa.PrimaryAdministrativeBoundary) 
                    .Where(oa => oa.IsActive);

                // --- APPLY DISPLAY LEVEL FILTER ---
                if (!string.IsNullOrWhiteSpace(displayLevel))
                {
                    // Case-insensitive comparison for displayLevel
                    query = query.Where(oa => oa.DisplayLevel != null && oa.DisplayLevel.ToLower() == displayLevel.ToLower());
                    logger.LogInformation("Filtering by DisplayLevel: {DisplayLevel}", displayLevel);
                }
                // --- END DISPLAY LEVEL FILTER ---

                var operationalAreas = await query
                    .OrderBy(oa => oa.NameEn)
                    .Select(oa => new OperationalAreaDto 
                    {
                        Id = oa.Id,
                        NameEn = oa.NameEn,
                        NameAr = oa.NameAr,
                        Slug = oa.Slug,
                        IsActive = oa.IsActive,
                        CentroidLatitude = oa.CentroidLatitude,
                        CentroidLongitude = oa.CentroidLongitude,
                        DefaultSearchRadiusMeters = oa.DefaultSearchRadiusMeters,
                        DefaultMapZoomLevel = oa.DefaultMapZoomLevel,
                        DisplayLevel = oa.DisplayLevel, // Map the DisplayLevel to the DTO
                        Geometry = (oa.GeometrySource == GeometrySourceType.Custom && oa.CustomSimplifiedBoundary != null)
                                   ? geoJsonWriter.Write(oa.CustomSimplifiedBoundary)
                                   : (oa.GeometrySource == GeometrySourceType.DerivedFromAdmin && oa.PrimaryAdministrativeBoundary != null && oa.PrimaryAdministrativeBoundary.SimplifiedBoundary != null)
                                       ? geoJsonWriter.Write(oa.PrimaryAdministrativeBoundary.SimplifiedBoundary)
                                       : null 
                    })
                    .ToListAsync();
                
                logger.LogInformation("Retrieved {Count} active operational areas for map.", operationalAreas.Count);
                return Results.Ok(operationalAreas);
            })
            .WithName("GetOperationalAreasForMap")
            .WithSummary("Get active operational areas with simplified geometry (as stringified GeoJSON) for map display, optionally filtered by displayLevel.")
            .Produces<List<OperationalAreaDto>>()
            .AllowAnonymous();
    // public static class GeneralEndpoints
    // {
    //     public static void MapGeneralEndpoints(this IEndpointRouteBuilder app)
    //     {
    //         var group = app.MapGroup("/api").WithTags("General");

    //         group.MapGet("/operational-areas-for-map", async (AppDbContext dbContext, ILogger<Program> logger) =>
    //         {
    //             logger.LogInformation("Fetching active operational areas with geometry for map display.");
    //             var geoJsonWriter = new GeoJsonWriter();

    //             var operationalAreas = await dbContext.OperationalAreas
    //                 .AsNoTracking()
    //                 .Include(oa => oa.PrimaryAdministrativeBoundary) 
    //                 .Where(oa => oa.IsActive)
    //                 .OrderBy(oa => oa.NameEn)
    //                 .Select(oa => new OperationalAreaDto 
    //                 {
    //                     Id = oa.Id,
    //                     NameEn = oa.NameEn,
    //                     NameAr = oa.NameAr,
    //                     Slug = oa.Slug,
    //                     IsActive = oa.IsActive,
    //                     CentroidLatitude = oa.CentroidLatitude,
    //                     CentroidLongitude = oa.CentroidLongitude,
    //                     DefaultSearchRadiusMeters = oa.DefaultSearchRadiusMeters,
    //                     DefaultMapZoomLevel = oa.DefaultMapZoomLevel,
    //                     Geometry = (oa.GeometrySource == GeometrySourceType.Custom && oa.CustomSimplifiedBoundary != null)
    //                                ? geoJsonWriter.Write(oa.CustomSimplifiedBoundary)
    //                                : (oa.GeometrySource == GeometrySourceType.DerivedFromAdmin && oa.PrimaryAdministrativeBoundary != null && oa.PrimaryAdministrativeBoundary.SimplifiedBoundary != null)
    //                                    ? geoJsonWriter.Write(oa.PrimaryAdministrativeBoundary.SimplifiedBoundary)
    //                                    : null 
    //                 })
    //                 .ToListAsync();
                
    //             logger.LogInformation("Retrieved {Count} active operational areas for map.", operationalAreas.Count);
    //             return Results.Ok(operationalAreas);
    //         })
    //         .WithName("GetOperationalAreasForMap")
    //         .WithSummary("Get active operational areas with simplified geometry (as stringified GeoJSON) for map display.")
    //         .Produces<List<OperationalAreaDto>>()
    //         .AllowAnonymous();

            group.MapGet("/cities", async (AppDbContext dbContext, ILogger<Program> logger) =>
            {
                logger.LogInformation("Fetching all active legacy cities using database view CityWithCoordinates.");
                var cities = await dbContext.CityWithCoordinates
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.NameEn)
                    .Select(c => new CityDto 
                    {
                        Id = c.Id, NameEn = c.NameEn, NameAr = c.NameAr, Slug = c.Slug,
                        StateProvince = c.StateProvince, Country = c.Country,
                        Latitude = c.Latitude, Longitude = c.Longitude, IsActive = c.IsActive,
                    })
                    .ToListAsync();
                logger.LogInformation("Retrieved {Count} active legacy cities.", cities.Count);
                return Results.Ok(cities);
            })
            .WithName("GetLegacyCities") 
            .WithSummary("Get all active legacy cities (primarily for reference or older parts of app).")
            .Produces<List<CityDto>>()
            .AllowAnonymous();

            group.MapGet("/operational-areas/{areaSlug}/subcategories",
                async (string areaSlug, [FromQuery] string? conceptFilterName, AppDbContext dbContext, ILogger<Program> logger) =>
            {
                logger.LogInformation("Fetching subcategories for OperationalArea: {AreaSlug}, Optional concept filter: {Concept}", areaSlug, conceptFilterName);

                var operationalArea = await dbContext.OperationalAreas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(oa => oa.Slug == areaSlug.ToLowerInvariant() && oa.IsActive);

                if (operationalArea == null)
                {
                    logger.LogWarning("OperationalArea not found or inactive: {AreaSlug}", areaSlug);
                    return Results.NotFound(new ProblemDetails { Title = "Operational Area Not Found", Detail = $"Operational Area '{areaSlug}' not found or is inactive.", Status = StatusCodes.Status404NotFound });
                }

                var subCategoriesFromDb = await dbContext.Shops
                    .AsNoTracking() 
                    .Where(s => s.OperationalAreaId == operationalArea.Id && s.Category != ShopCategory.Unknown)
                    .GroupBy(s => s.Category) 
                    .Select(g => new
                    {
                        CategoryEnumMember = g.Key,
                        ShopCount = g.Count()
                    })
                    .ToListAsync();

                var resultDtos = subCategoriesFromDb.Select(sc => new SubCategoryDto 
                {
                    SubCategoryEnum = sc.CategoryEnumMember,
                    Name = sc.CategoryEnumMember.ToString(),    
                    Slug = CategoryInfo.GetSlug(sc.CategoryEnumMember), 
                    ShopCount = sc.ShopCount,
                    Concept = CategoryInfo.GetConcept(sc.CategoryEnumMember) 
                })
                .OrderBy(dto => dto.Concept)
                .ThenBy(dto => dto.Name)
                .ToList();

                if (!string.IsNullOrWhiteSpace(conceptFilterName) &&
                    Enum.TryParse<HighLevelConcept>(conceptFilterName, true, out var parsedConceptEnum)) 
                {
                    // --- CORRECTED ENUM COMPARISON ---
                    if (parsedConceptEnum != HighLevelConcept.Unknown) // Enums can be compared directly with != or ==
                    {
                        // And using .Equals() is also robust
                        resultDtos = resultDtos.Where(dto => dto.Concept.Equals(parsedConceptEnum)).ToList();
                        // Or compare underlying values:
                        // resultDtos = resultDtos.Where(dto => (int)dto.Concept == (int)parsedConceptEnum).ToList();
                        logger.LogInformation("Filtered subcategories by concept: {Concept}", parsedConceptEnum);
                    }
                    // --- END CORRECTION ---
                } // This curly brace closes the if block for conceptFilterName

                logger.LogInformation("Found {Count} subcategories for OperationalArea {AreaSlug}.", resultDtos.Count, areaSlug);
                return Results.Ok(resultDtos);
            }) // This closes MapGet
            .WithName("GetSubCategoriesByOperationalArea")
            .WithSummary("Get subcategories with shop counts for a specific operational area, optionally filtered by high-level concept.")
            .Produces<List<SubCategoryDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous(); // This semicolon was potentially missing if the "}" expected error was here
        } // This curly brace closes MapGeneralEndpoints method
    } // This curly brace closes the class
} // This curly brace closes the namespace