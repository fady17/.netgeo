// src/AutomotiveServices.Api/Endpoints/Features/Shops/ShopQueryEndpoints.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models;
using AutomotiveServices.Api.Validation;
using LinqKit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Endpoints.Features.Shops
{
    public class PaginatedResponse<T>
    {
        public List<T> Data { get; set; } = new List<T>();
        public PaginationMetadata Pagination { get; set; } = new PaginationMetadata();
    }

    public class PaginationMetadata
    {
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    file record ShopDetailsViewWithDistance(ShopDetailsView View, double? DistanceInMetersFromDb);

    public static class ShopQueryEndpoints
    {
        // Route group should be created like:
        // var operationalAreasGroup = app.MapGroup("/api/operational-areas");
        // var categoriesGroup = operationalAreasGroup.MapGroup("/{areaSlug}/categories");
        // var shopsFeatureGroup = categoriesGroup.MapGroup("/{subCategorySlug}/shops");
        // ShopQueryEndpoints.MapShopQueryEndpoints(shopsFeatureGroup);
        public static void MapShopQueryEndpoints(this RouteGroupBuilder group)
        {
            group.WithTags("Shops Query"); 

            group.MapGet("/", async (
                string areaSlug,       // Route parameter from parent group
                string subCategorySlug,  // Route parameter from parent group
                [AsParameters] ShopQueryParameters queryParams,
                AppDbContext dbContext,
                ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("ShopsApi.ListByOperationalAreaAndSubCategory");
                logger.LogInformation(
                    "Fetching shops for OperationalArea Slug: {AreaSlug}, SubCategorySlug: {SubCategorySlug}, Query: {@QueryParams}",
                    areaSlug, subCategorySlug, queryParams);

                var operationalArea = await dbContext.OperationalAreas.AsNoTracking()
                    .Where(oa => oa.Slug == areaSlug.ToLowerInvariant() && oa.IsActive)
                    .Select(oa => new { oa.Id, oa.NameEn }) 
                    .FirstOrDefaultAsync();

                if (operationalArea == null)
                {
                    return Results.NotFound(new ProblemDetails { Title = "Operational Area Not Found", Detail = $"Operational Area '{areaSlug}' not found or is inactive.", Status = StatusCodes.Status404NotFound });
                }

                if (!CategoryInfo.IsValidSubCategorySlug(subCategorySlug))
                {
                    return Results.BadRequest(new ProblemDetails { Title = "Invalid Subcategory", Detail = $"Subcategory '{subCategorySlug}' is not valid.", Status = StatusCodes.Status400BadRequest });
                }
                var subCategoryEnum = CategoryInfo.GetSubCategory(subCategorySlug);

                // IMPORTANT: Assumes ShopDetailsView.OperationalAreaId is populated correctly
                // AND ShopDetailsView.Category is the ShopCategory enum
                var query = dbContext.ShopDetailsView.AsNoTracking().AsExpandable()
                    .Where(v => v.OperationalAreaId == operationalArea.Id && v.Category == subCategoryEnum && !v.IsDeleted);

                if (!string.IsNullOrWhiteSpace(queryParams.Name))
                {
                    var nameTerm = $"%{queryParams.Name.Trim()}%";
                    query = query.Where(v => EF.Functions.ILike(v.NameEn, nameTerm) || EF.Functions.ILike(v.NameAr, nameTerm));
                }

                if (!string.IsNullOrWhiteSpace(queryParams.Services))
                {
                    var serviceFilters = queryParams.Services.Split(',')
                        .Select(sFilter => sFilter.Trim())
                        .Where(sFilter => !string.IsNullOrWhiteSpace(sFilter))
                        .ToList();
                    if (serviceFilters.Any())
                    {
                        var predicate = PredicateBuilder.New<ShopDetailsView>(false);
                        foreach (var service in serviceFilters)
                        {
                            var tempService = $"%{service}%";
                            predicate = predicate.Or(v => v.ServicesOffered != null && EF.Functions.ILike(v.ServicesOffered, tempService));
                        }
                        query = query.Where(predicate);
                    }
                }

                Point? userLocationPoint = null;
                if (queryParams.UserLatitude.HasValue && queryParams.UserLongitude.HasValue)
                {
                    userLocationPoint = new Point(queryParams.UserLongitude.Value, queryParams.UserLatitude.Value) { SRID = 4326 };
                    if (queryParams.RadiusInMeters.HasValue && queryParams.RadiusInMeters.Value > 0)
                    {
                        // Assumes ShopDetailsView.Location is the shop's Point geometry
                        query = query.Where(v => v.Location.IsWithinDistance(userLocationPoint, queryParams.RadiusInMeters.Value));
                    }
                }

                var totalItems = await query.CountAsync();
                var effectivePageNumber = queryParams.GetEffectivePageNumber();
                var effectivePageSize = queryParams.GetEffectivePageSize();

                IQueryable<ShopDetailsViewWithDistance> queryWithProjectedDistance;
                string sortBy = queryParams.SortBy?.Trim().ToLowerInvariant() ?? "";

                if (userLocationPoint != null && (sortBy == "distance_asc" || (string.IsNullOrEmpty(sortBy) && queryParams.UserLatitude.HasValue)))
                {
                    queryWithProjectedDistance = query
                        .OrderBy(v => v.Location.Distance(userLocationPoint))
                        .Select(v => new ShopDetailsViewWithDistance(v, (double?)v.Location.Distance(userLocationPoint)));
                }
                else
                {
                    IOrderedQueryable<ShopDetailsView> orderedQuery;
                    if (sortBy == "name_asc") orderedQuery = query.OrderBy(v => v.NameEn);
                    else if (sortBy == "name_desc") orderedQuery = query.OrderByDescending(v => v.NameEn);
                    else orderedQuery = query.OrderBy(v => v.NameEn); 

                    queryWithProjectedDistance = orderedQuery
                        .Select(v => new ShopDetailsViewWithDistance(v, userLocationPoint != null ? (double?)v.Location.Distance(userLocationPoint) : null));
                }

                var pagedResults = await queryWithProjectedDistance
                    .Skip((effectivePageNumber - 1) * effectivePageSize)
                    .Take(effectivePageSize)
                    .ToListAsync();

                var resultDtos = pagedResults.Select(item => new ShopDto 
                {
                    Id = item.View.Id, NameEn = item.View.NameEn, NameAr = item.View.NameAr,
                    Slug = item.View.ShopSlug, 
                    LogoUrl = item.View.LogoUrl,
                    DescriptionEn = item.View.DescriptionEn, DescriptionAr = item.View.DescriptionAr,
                    Address = item.View.Address, Latitude = item.View.ShopLatitude, Longitude = item.View.ShopLongitude,
                    PhoneNumber = item.View.PhoneNumber, ServicesOffered = item.View.ServicesOffered,
                    OpeningHours = item.View.OpeningHours, 
                    Category = item.View.Category, // This is ShopCategory from the view
                    
                    OperationalAreaId = item.View.OperationalAreaId, // From updated ShopDetailsView
                    OperationalAreaNameEn = item.View.OperationalAreaNameEn, // From updated ShopDetailsView
                    OperationalAreaNameAr = item.View.OperationalAreaNameAr, // From updated ShopDetailsView
                    OperationalAreaSlug = item.View.OperationalAreaSlug,   // From updated ShopDetailsView
                    
                    DistanceInMeters = item.DistanceInMetersFromDb
                    // Concept is now a get-only property in ShopDto, calculated using CategoryInfo
                }).ToList();

                var pagination = new PaginationMetadata
                {
                    TotalCount = totalItems, PageSize = effectivePageSize, CurrentPage = effectivePageNumber,
                    TotalPages = (int)Math.Ceiling(totalItems / (double)effectivePageSize),
                    HasPreviousPage = effectivePageNumber > 1,
                    HasNextPage = effectivePageNumber < (int)Math.Ceiling(totalItems / (double)effectivePageSize)
                };

                logger.LogInformation("Retrieved {ShopCount} shops for OperationalArea: {AreaSlug}, SubCategory: {SubCategorySlug}", resultDtos.Count, areaSlug, subCategorySlug);
                return Results.Ok(new PaginatedResponse<ShopDto> { Data = resultDtos, Pagination = pagination });
            })
            .AddEndpointFilter<ValidationFilter<ShopQueryParameters>>() 
            .WithName("GetShopsByOperationalAreaAndSubCategory") 
            .WithSummary("Get shops by operational area and subcategory (uses optimized view).")
            .Produces<PaginatedResponse<ShopDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

            group.MapGet("/{shopId:guid}", async (
                string areaSlug,        // Route parameter from parent group
                string subCategorySlug, // Route parameter from parent group
                Guid shopId,
                AppDbContext dbContext,
                ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("ShopsApi.GetShopDetailsByArea.View");
                logger.LogInformation(
                   "Fetching shop details for ID: {ShopId}, OperationalArea: {AreaSlug}, SubCategorySlug: {SubCategorySlug}",
                   shopId, areaSlug, subCategorySlug);

                var operationalArea = await dbContext.OperationalAreas.AsNoTracking()
                    .Where(oa => oa.Slug == areaSlug.ToLowerInvariant() && oa.IsActive)
                    .Select(oa => new { oa.Id }) 
                    .FirstOrDefaultAsync();

                if (operationalArea == null) 
                {
                    return Results.NotFound(new ProblemDetails { Title = "Context Operational Area Not Found", Detail = $"Context operational area '{areaSlug}' not found.", Status = StatusCodes.Status404NotFound });
                }

                if (!CategoryInfo.IsValidSubCategorySlug(subCategorySlug)) 
                {
                    return Results.BadRequest(new ProblemDetails { Title = "Invalid Context Subcategory", Detail = $"Context subcategory '{subCategorySlug}' is not valid.", Status = StatusCodes.Status400BadRequest });
                }
                var subCategoryEnum = CategoryInfo.GetSubCategory(subCategorySlug);

                // IMPORTANT: Assumes ShopDetailsView.OperationalAreaId and ShopDetailsView.Category are correct
                var shopViewResult = await dbContext.ShopDetailsView.AsNoTracking()
                    .Where(v => v.Id == shopId && 
                                v.OperationalAreaId == operationalArea.Id && 
                                v.Category == subCategoryEnum && 
                                !v.IsDeleted)
                    .FirstOrDefaultAsync();

                if (shopViewResult == null)
                {
                    return Results.NotFound(new ProblemDetails { Title = "Shop Not Found", Detail = "Shop not found or does not match the specified operational area/category context.", Status = StatusCodes.Status404NotFound });
                }

                var shopDtoResult = new ShopDto
                {
                    Id = shopViewResult.Id,
                    NameEn = shopViewResult.NameEn,
                    NameAr = shopViewResult.NameAr,
                    Slug = shopViewResult.ShopSlug,
                    LogoUrl = shopViewResult.LogoUrl,
                    DescriptionEn = shopViewResult.DescriptionEn,
                    DescriptionAr = shopViewResult.DescriptionAr,
                    Address = shopViewResult.Address,
                    Latitude = shopViewResult.ShopLatitude,
                    Longitude = shopViewResult.ShopLongitude,
                    PhoneNumber = shopViewResult.PhoneNumber,
                    ServicesOffered = shopViewResult.ServicesOffered,
                    OpeningHours = shopViewResult.OpeningHours,
                    Category = shopViewResult.Category,
                    
                    OperationalAreaId = shopViewResult.OperationalAreaId,
                    OperationalAreaNameEn = shopViewResult.OperationalAreaNameEn,
                    OperationalAreaNameAr = shopViewResult.OperationalAreaNameAr,
                    OperationalAreaSlug = shopViewResult.OperationalAreaSlug,
                    
                    DistanceInMeters = null 
                    // Concept is a get-only property in ShopDto
                };

                return Results.Ok(shopDtoResult);
            })
            .WithName("GetShopDetailsByArea") 
            .WithSummary("Get specific shop details by ID within operational area/subcategory context (uses optimized view).")
            .Produces<ShopDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
        }
    }
}
// // src/AutomotiveServices.Api/Endpoints/Features/Shops/ShopQueryEndpoints.cs
// using AutomotiveServices.Api.Data;
// using AutomotiveServices.Api.Dtos;
// using AutomotiveServices.Api.Models;
// using AutomotiveServices.Api.Validation;
// using LinqKit;
// using Microsoft.AspNetCore.Builder; // Required for IEndpointRouteBuilder
// using Microsoft.AspNetCore.Http;
// using Microsoft.AspNetCore.Routing; // Required for RouteGroupBuilder
// using Microsoft.AspNetCore.Mvc; // For [AsParameters] and ProblemDetails
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using NetTopologySuite.Geometries;
// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading.Tasks;

// namespace AutomotiveServices.Api.Endpoints.Features.Shops;

// // Keep PaginatedResponse and PaginationMetadata here if they are primarily used by shop queries
// // Or move them to a shared DTOs/Common folder if used more broadly. For now, keep here.
// public class PaginatedResponse<T>
// {
//     public List<T> Data { get; set; } = new List<T>();
//     public PaginationMetadata Pagination { get; set; } = new PaginationMetadata();
// }

// public class PaginationMetadata
// {
//     public int TotalCount { get; set; }
//     public int PageSize { get; set; }
//     public int CurrentPage { get; set; }
//     public int TotalPages { get; set; }
//     public bool HasPreviousPage { get; set; }
//     public bool HasNextPage { get; set; }
// }

// file record ShopDetailsViewWithDistance(ShopDetailsView View, double? DistanceInMetersFromDb);

// public static class ShopQueryEndpoints
// {
//     // This method will be called on the specific group for shops
//     public static void MapShopQueryEndpoints(this RouteGroupBuilder group)
//     {
//         group.WithTags("Shops Query"); // Tag for this specific set of endpoints

//         group.MapGet("/", async (
//             // citySlug and subCategorySlug are now available from the route context
//             // if this 'group' was created with them as parameters.
//             // For Minimal APIs, parameters in MapGroup are automatically passed if named same.
//             string citySlug,
//             string subCategorySlug,
//             [AsParameters] ShopQueryParameters queryParams,
//             AppDbContext dbContext,
//             ILoggerFactory loggerFactory) =>
//         {
//             var logger = loggerFactory.CreateLogger("ShopsApi.ListByCitySubCategoryView");
//             logger.LogInformation(
//                 "Fetching shops for CitySlug: {CitySlug}, SubCategorySlug: {SubCategorySlug}, Query: {@QueryParams}",
//                 citySlug, subCategorySlug, queryParams);

//             var city = await dbContext.Cities.AsNoTracking()
//                 .Where(c => c.Slug == citySlug.ToLowerInvariant() && c.IsActive)
//                 .Select(c => new { c.Id, c.NameEn })
//                 .FirstOrDefaultAsync();

//             if (city == null)
//             {
//                 return Results.NotFound(new ProblemDetails { Title = "City Not Found", Detail = $"City '{citySlug}' not found or is inactive.", Status = StatusCodes.Status404NotFound });
//             }

//             if (!CategoryInfo.IsValidSubCategorySlug(subCategorySlug))
//             {
//                 return Results.BadRequest(new ProblemDetails { Title = "Invalid Subcategory", Detail = $"Subcategory '{subCategorySlug}' is not valid.", Status = StatusCodes.Status400BadRequest });
//             }
//             var subCategoryEnum = CategoryInfo.GetSubCategory(subCategorySlug);

//             var query = dbContext.ShopDetailsView.AsNoTracking().AsExpandable()
//                 .Where(v => v.CityId == city.Id && v.Category == subCategoryEnum && !v.IsDeleted);

//             if (!string.IsNullOrWhiteSpace(queryParams.Name))
//             {
//                 var nameTerm = $"%{queryParams.Name.Trim()}%";
//                 query = query.Where(v => EF.Functions.ILike(v.NameEn, nameTerm) || EF.Functions.ILike(v.NameAr, nameTerm));
//             }

//             if (!string.IsNullOrWhiteSpace(queryParams.Services))
//             {
//                 var serviceFilters = queryParams.Services.Split(',')
//                     .Select(sFilter => sFilter.Trim())
//                     .Where(sFilter => !string.IsNullOrWhiteSpace(sFilter))
//                     .ToList();
//                 if (serviceFilters.Any())
//                 {
//                     var predicate = PredicateBuilder.New<ShopDetailsView>(false);
//                     foreach (var service in serviceFilters)
//                     {
//                         var tempService = $"%{service}%";
//                         predicate = predicate.Or(v => v.ServicesOffered != null && EF.Functions.ILike(v.ServicesOffered, tempService));
//                     }
//                     query = query.Where(predicate);
//                 }
//             }

//             Point? userLocationPoint = null;
//             if (queryParams.UserLatitude.HasValue && queryParams.UserLongitude.HasValue)
//             {
//                 userLocationPoint = new Point(queryParams.UserLongitude.Value, queryParams.UserLatitude.Value) { SRID = 4326 };
//                 if (queryParams.RadiusInMeters.HasValue && queryParams.RadiusInMeters.Value > 0)
//                 {
//                     query = query.Where(v => v.Location.IsWithinDistance(userLocationPoint, queryParams.RadiusInMeters.Value));
//                 }
//             }

//             var totalItems = await query.CountAsync();
//             var effectivePageNumber = queryParams.GetEffectivePageNumber();
//             var effectivePageSize = queryParams.GetEffectivePageSize();

//             IQueryable<ShopDetailsViewWithDistance> queryWithProjectedDistance;
//             string sortBy = queryParams.SortBy?.Trim().ToLowerInvariant() ?? "";

//             if (userLocationPoint != null && (sortBy == "distance_asc" || (string.IsNullOrEmpty(sortBy) && queryParams.UserLatitude.HasValue)))
//             {
//                 queryWithProjectedDistance = query
//                     .OrderBy(v => v.Location.Distance(userLocationPoint))
//                     .Select(v => new ShopDetailsViewWithDistance(v, (double?)v.Location.Distance(userLocationPoint)));
//             }
//             else
//             {
//                 IOrderedQueryable<ShopDetailsView> orderedQuery;
//                 if (sortBy == "name_asc") orderedQuery = query.OrderBy(v => v.NameEn);
//                 else if (sortBy == "name_desc") orderedQuery = query.OrderByDescending(v => v.NameEn);
//                 else orderedQuery = query.OrderBy(v => v.NameEn); // Default sort

//                 queryWithProjectedDistance = orderedQuery
//                     .Select(v => new ShopDetailsViewWithDistance(v, userLocationPoint != null ? (double?)v.Location.Distance(userLocationPoint) : null));
//             }

//             var pagedResults = await queryWithProjectedDistance
//                 .Skip((effectivePageNumber - 1) * effectivePageSize)
//                 .Take(effectivePageSize)
//                 .ToListAsync();

//             var resultDtos = pagedResults.Select(item => new ShopDto // ShopDto.ServicesOffered string is kept for now
//             {
//                 Id = item.View.Id, NameEn = item.View.NameEn, NameAr = item.View.NameAr,
//                 Slug = item.View.ShopSlug, LogoUrl = item.View.LogoUrl,
//                 DescriptionEn = item.View.DescriptionEn, DescriptionAr = item.View.DescriptionAr,
//                 Address = item.View.Address, Latitude = item.View.ShopLatitude, Longitude = item.View.ShopLongitude,
//                 PhoneNumber = item.View.PhoneNumber, ServicesOffered = item.View.ServicesOffered,
//                 OpeningHours = item.View.OpeningHours, SubCategory = item.View.Category,
//                 CityId = item.View.CityId, DistanceInMeters = item.DistanceInMetersFromDb
//             }).ToList();

//             var pagination = new PaginationMetadata
//             {
//                 TotalCount = totalItems, PageSize = effectivePageSize, CurrentPage = effectivePageNumber,
//                 TotalPages = (int)Math.Ceiling(totalItems / (double)effectivePageSize),
//                 HasPreviousPage = effectivePageNumber > 1,
//                 HasNextPage = effectivePageNumber < (int)Math.Ceiling(totalItems / (double)effectivePageSize)
//             };

//             logger.LogInformation("Retrieved {ShopCount} shops for City: {CitySlug}, SubCategory: {SubCategorySlug}", resultDtos.Count, citySlug, subCategorySlug);
//             return Results.Ok(new PaginatedResponse<ShopDto> { Data = resultDtos, Pagination = pagination });
//         })
//         .AddEndpointFilter<ValidationFilter<ShopQueryParameters>>() // Assuming ValidationFilter is accessible
//         .WithName("GetShopsByCityAndSubCategory")
//         .WithSummary("Get shops by city and subcategory (uses optimized view).")
//         .Produces<PaginatedResponse<ShopDto>>()
//         .ProducesProblem(StatusCodes.Status404NotFound)
//         .ProducesProblem(StatusCodes.Status400BadRequest)
//         .AllowAnonymous();

//         group.MapGet("/{shopId:guid}", async (
//             // citySlug and subCategorySlug are available from the route context
//             string citySlug,
//             string subCategorySlug,
//             Guid shopId,
//             AppDbContext dbContext,
//             ILoggerFactory loggerFactory) =>
//         {
//             var logger = loggerFactory.CreateLogger("ShopsApi.GetShopDetails.View");
//             logger.LogInformation(
//                "Fetching shop details for ID: {ShopId}, CitySlug: {CitySlug}, SubCategorySlug: {SubCategorySlug}",
//                shopId, citySlug, subCategorySlug);

//             var city = await dbContext.Cities.AsNoTracking()
//                 .Where(c => c.Slug == citySlug.ToLowerInvariant() && c.IsActive)
//                 .Select(c => new { c.Id })
//                 .FirstOrDefaultAsync();
//             if (city == null) return Results.NotFound(new ProblemDetails { Title = "Context City Not Found", Detail = $"Context city '{citySlug}' not found.", Status = StatusCodes.Status404NotFound });

//             if (!CategoryInfo.IsValidSubCategorySlug(subCategorySlug)) return Results.BadRequest(new ProblemDetails { Title = "Invalid Context Subcategory", Detail = $"Context subcategory '{subCategorySlug}' is not valid.", Status = StatusCodes.Status400BadRequest });
//             var subCategoryEnum = CategoryInfo.GetSubCategory(subCategorySlug);

//             var shopDtoResult = await dbContext.ShopDetailsView.AsNoTracking()
//                 .Where(v => v.Id == shopId && v.CityId == city.Id && v.Category == subCategoryEnum && !v.IsDeleted)
//                 .Select(v => new ShopDto
//                 {
//                     Id = v.Id, NameEn = v.NameEn, NameAr = v.NameAr, Slug = v.ShopSlug, LogoUrl = v.LogoUrl,
//                     DescriptionEn = v.DescriptionEn, DescriptionAr = v.DescriptionAr, Address = v.Address,
//                     Latitude = v.ShopLatitude, Longitude = v.ShopLongitude, PhoneNumber = v.PhoneNumber,
//                     ServicesOffered = v.ServicesOffered, // Kept for now
//                     OpeningHours = v.OpeningHours, SubCategory = v.Category,
//                     CityId = v.CityId, DistanceInMeters = null
//                 })
//                 .FirstOrDefaultAsync();

//             if (shopDtoResult == null)
//             {
//                 return Results.NotFound(new ProblemDetails { Title = "Shop Not Found", Detail = "Shop not found or does not match the specified city/category context.", Status = StatusCodes.Status404NotFound });
//             }

//             return Results.Ok(shopDtoResult);
//         })
//         .WithName("GetShopDetails") // Simplified name as it's on the specific shop ID route
//         .WithSummary("Get specific shop details by ID within city/subcategory context (uses optimized view).")
//         .Produces<ShopDto>()
//         .ProducesProblem(StatusCodes.Status404NotFound)
//         .ProducesProblem(StatusCodes.Status400BadRequest)
//         .AllowAnonymous();
//     }
// }