// src/AutomotiveServices.Api/Dtos/ShopQueryParameters.cs
using System.ComponentModel.DataAnnotations;
using AutomotiveServices.Api.Models;

namespace AutomotiveServices.Api.Dtos;

public class ShopQueryParameters
{
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 10; 
    public const int DefaultPageNumber = 1;

    public const int MaxRadiusInMeters =  1010408000;   // EGYPT

    [MaxLength(100, ErrorMessage = "Name search term cannot exceed 100 characters.")]
    public string? Name { get; set; }

    // public ShopCategory? Category { get; set; } // Nullable for "all categories"

    public string? Services { get; set; } // For text search on ServicesOffered within the category
    

    [Range(-90.0, 90.0, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double? UserLatitude { get; set; }

    [Range(-180.0, 180.0, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double? UserLongitude { get; set; }

    // Corrected ErrorMessage to be a compile-time constant string
    [Range(1, MaxRadiusInMeters, ErrorMessage = "If provided, Radius must be between 1 and 50000 meters.")]
    public int? RadiusInMeters { get; set; }

    [Range(1, int.MaxValue)]
    public int? PageNumber { get; set; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; set; }

    public string? SortBy { get; set; }

    public int GetEffectivePageNumber() => PageNumber.HasValue && PageNumber.Value > 0 ? PageNumber.Value : DefaultPageNumber;

    public int GetEffectivePageSize()
    {
        if (!PageSize.HasValue || PageSize.Value <= 0) return DefaultPageSize;
        return PageSize.Value > MaxPageSize ? MaxPageSize : PageSize.Value;
    }
}
// // src/AutomotiveServices.Api/Dtos/ShopQueryParameters.cs
// using System.ComponentModel.DataAnnotations;

// namespace AutomotiveServices.Api.Dtos;

// public class ShopQueryParameters
// {
//     public const int MaxPageSize = 50;
//     public const int DefaultPageSize = 10;
//     public const int DefaultPageNumber = 1;

//     public string? Name { get; set; }
//     public string? Services { get; set; }

//     [Range(-90.0, 90.0)]
//     public double? UserLatitude { get; set; }

//     [Range(-180.0, 180.0)]
//     public double? UserLongitude { get; set; }

//     [Range(1, 100000)] // Max radius in meters
//     public int? RadiusInMeters { get; set; }

//     // Make PageNumber nullable
//     [Range(1, int.MaxValue)]
//     public int? PageNumber { get; set; }

//     // Make PageSize nullable
//     [Range(1, MaxPageSize)]
//     public int? PageSize { get; set; }

//     public string? SortBy { get; set; }

//     // Helper methods to get effective values with defaults
//     public int GetEffectivePageNumber() => PageNumber ?? DefaultPageNumber;

//     public int GetEffectivePageSize()
//     {
//         if (!PageSize.HasValue) return DefaultPageSize;
//         return PageSize.Value > MaxPageSize ? MaxPageSize : PageSize.Value;
//     }
// }