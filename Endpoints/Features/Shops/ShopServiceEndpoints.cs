// src/AutomotiveServices.Api/Endpoints/Features/Shops/ShopServiceEndpoints.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models; // For CategoryInfo if needed for context validation, though shopId is primary
using Microsoft.AspNetCore.Builder; // Required for IEndpointRouteBuilder
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing; // Required for RouteGroupBuilder
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc; // For ProblemDetails

namespace AutomotiveServices.Api.Endpoints.Features.Shops;

public static class ShopServiceEndpoints
{
    public static void MapShopServiceEndpoints(this RouteGroupBuilder shopGroup) // Takes the shop-specific group
    {
        // The 'shopGroup' is already prefixed with:
        // /api/cities/{citySlug}/categories/{subCategorySlug}/shops

        shopGroup.MapGet("/{shopId:guid}/services", async (
            Guid shopId,
            // string citySlug, // Access from route context if needed for validation
            // string subCategorySlug, // Access from route context if needed for validation
            AppDbContext dbContext,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ShopsApi.GetShopServices");
            logger.LogInformation("Fetching services for ShopId: {ShopId}", shopId);

            // Optional: Validate shopId against citySlug/subCategorySlug if strict contextual integrity is needed
            // This adds overhead but ensures the shopId belongs to the path context.
            // For now, assuming shopId is globally unique and sufficient.
            // If validation is needed:
            // var routeValues = context.GetRouteData().Values;
            // var citySlugFromRoute = routeValues["citySlug"]?.ToString();
            // var subCategorySlugFromRoute = routeValues["subCategorySlug"]?.ToString();
            // ... then query ShopDetailsView to check if shopId, citySlug, subCategorySlug match ...
            // This makes the query more complex. For now, we'll trust shopId.

            var shopExists = await dbContext.Shops
                .AnyAsync(s => s.Id == shopId /* Global query filter for IsDeleted applies */);

            if (!shopExists)
            {
                logger.LogWarning("Shop not found: {ShopId} when fetching services.", shopId);
                return Results.NotFound(new ProblemDetails { Title = "Shop Not Found", Detail = $"Shop with ID '{shopId}' not found.", Status = StatusCodes.Status404NotFound });
            }

            var services = await dbContext.ShopServices
                .AsNoTracking()
                .Where(ss => ss.ShopId == shopId && ss.IsOfferedByShop)
                .Include(ss => ss.GlobalServiceDefinition)
                .OrderBy(ss => ss.SortOrder)
                .ThenBy(ss => ss.EffectiveNameEn)
                .Select(ss => new ShopServiceDto
                {
                    ShopServiceId = ss.ShopServiceId,
                    ShopId = ss.ShopId,
                    NameEn = ss.EffectiveNameEn,
                    NameAr = ss.EffectiveNameAr,
                    DescriptionEn = !string.IsNullOrEmpty(ss.ShopSpecificDescriptionEn)
                                    ? ss.ShopSpecificDescriptionEn
                                    : ss.GlobalServiceDefinition != null ? ss.GlobalServiceDefinition.DefaultDescriptionEn : null,
                    DescriptionAr = !string.IsNullOrEmpty(ss.ShopSpecificDescriptionAr)
                                    ? ss.ShopSpecificDescriptionAr
                                    : ss.GlobalServiceDefinition != null ? ss.GlobalServiceDefinition.DefaultDescriptionAr : null,
                    Price = ss.Price,
                    DurationMinutes = ss.DurationMinutes ?? (ss.GlobalServiceDefinition != null ? ss.GlobalServiceDefinition.DefaultEstimatedDurationMinutes : null),
                    IconUrl = !string.IsNullOrEmpty(ss.ShopSpecificIconUrl)
                                ? ss.ShopSpecificIconUrl
                                : ss.GlobalServiceDefinition != null ? ss.GlobalServiceDefinition.DefaultIconUrl : null,
                    IsPopularAtShop = ss.IsPopularAtShop,
                    SortOrder = ss.SortOrder,
                    GlobalServiceId = ss.GlobalServiceId,
                    GlobalServiceCode = ss.GlobalServiceDefinition != null ? ss.GlobalServiceDefinition.ServiceCode : null
                })
                .ToListAsync();

            // No need to log if empty, frontend can handle empty list
            return Results.Ok(services);
        })
        .WithName("GetServicesByShop") // Changed from GetServicesByShopId for clarity, as shopId is implicit in group
        .WithTags("Shop Services") // New tag
        .WithSummary("Get all active services offered by a specific shop.")
        .Produces<List<ShopServiceDto>>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .AllowAnonymous();
    }
}