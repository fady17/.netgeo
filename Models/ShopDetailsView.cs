// File: Models/ShopDetailsView.cs (Updated)
using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries; // For Point

namespace AutomotiveServices.Api.Models
{
    public class ShopDetailsView
    {
        // Columns directly from the Shops table (or direct mappings)
        public Guid Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? ShopSlug { get; set; } // From Shops.Slug
        public string? LogoUrl { get; set; }
        public string? DescriptionEn { get; set; }
        public string? DescriptionAr { get; set; }
        public string Address { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? ServicesOffered { get; set; } // Still here, assuming it might be used
        public string? OpeningHours { get; set; }
        public ShopCategory Category { get; set; } // From Shops.Category
        public bool IsDeleted { get; set; } // From Shops.IsDeleted

        // Pre-calculated/projected from Shop.Location
        public double ShopLatitude { get; set; }
        public double ShopLongitude { get; set; }
        public Point Location { get; set; } = null!; // Original Point geometry from Shops.Location

        // --- NEW/UPDATED Properties from OperationalArea ---
        [Required]
        public int OperationalAreaId { get; set; } // Foreign Key from Shops.OperationalAreaId

        [Required]
        [MaxLength(150)] // Should match OperationalArea.NameEn MaxLength
        public string OperationalAreaNameEn { get; set; } = string.Empty; // From OperationalAreas.NameEn

        [Required]
        [MaxLength(150)] // Should match OperationalArea.NameAr MaxLength
        public string OperationalAreaNameAr { get; set; } = string.Empty; // From OperationalAreas.NameAr

        [Required]
        [MaxLength(150)] // Should match OperationalArea.Slug MaxLength
        public string OperationalAreaSlug { get; set; } = string.Empty; // From OperationalAreas.Slug

        // Optional: If your view also joins to AdministrativeBoundaries via OperationalArea
        // public string? GovernorateNameEn { get; set; } 
        // public string? CountryCode { get; set; }
    }
}
// // src/AutomotiveServices.Api/Models/ShopDetailsView.cs
// using NetTopologySuite.Geometries; // Only if you need to expose the Point object itself from the view, usually not needed for DTO mapping.

// namespace AutomotiveServices.Api.Models;

// public class ShopDetailsView
// {
//     // Columns directly from the Shops table
//     public Guid Id { get; set; }
//     public string NameEn { get; set; } = string.Empty;
//     public string NameAr { get; set; } = string.Empty;
//     public string? ShopSlug { get; set; }
//     public string? LogoUrl { get; set; }
//     public string? DescriptionEn { get; set; }
//     public string? DescriptionAr { get; set; }
//     public string Address { get; set; } = string.Empty;
//     public string? PhoneNumber { get; set; }
//     public string? ServicesOffered { get; set; }
//     public string? OpeningHours { get; set; }
//     public ShopCategory Category { get; set; } // This is our SubCategory
//                                                // public int CityId { get; set; }
//     public int OperationalAreaId { get; set; }
    
    
//     public bool IsDeleted { get; set; } // Keep for potential filtering if view isn't pre-filtered

//     // Pre-calculated/projected from Shop.Location
//     public double ShopLatitude { get; set; }
//     public double ShopLongitude { get; set; }

//     // We will NOT calculate DistanceInMeters in the view itself
//     // because distance is dynamic based on user input (UserLatitude, UserLongitude).
//     // The view will provide the shop's location (as Point or separate lat/lon)
//     // and the distance calculation will happen in the query AGAINST the view,
//     // or if performance is critical and the DB supports it, the view could be a Table-Valued Function.
//     // For simplicity, let's have the view expose the Point for distance calculation in EF Core.
//     public Point Location { get; set; } = null!; // Expose the original Location Point for distance functions
// }