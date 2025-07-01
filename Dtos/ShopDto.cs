// src/AutomotiveServices.Api/Dtos/ShopDto.cs
using System.ComponentModel.DataAnnotations;
using AutomotiveServices.Api.Models; // For Enums

namespace AutomotiveServices.Api.Dtos
{
    public class ShopDto
    {
        public Guid Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string? Slug { get; set; } // Shop's own slug
        public string? LogoUrl { get; set; }

        public string? DescriptionEn { get; set; }
        public string? DescriptionAr { get; set; }
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; } // Shop's specific latitude
        public double Longitude { get; set; } // Shop's specific longitude
        public string? PhoneNumber { get; set; }
        public string? ServicesOffered { get; set; } // Denormalized string, if still used
        public string? OpeningHours { get; set; }
        
        // Shop's Primary Category Information
        public ShopCategory Category { get; set; } // e.g., ShopCategory.OilChange
        public string CategoryName => Category.ToString(); 
        public string CategorySlug => CategoryInfo.GetSlug(Category); 
        public Models.HighLevelConcept Concept => CategoryInfo.GetConcept(Category);

        // Operational Area Information (Replaces CityId)
        [Required]
        public int OperationalAreaId { get; set; }
        public string OperationalAreaNameEn { get; set; } = string.Empty;
        public string OperationalAreaNameAr { get; set; } = string.Empty;
        public string OperationalAreaSlug { get; set; } = string.Empty;
        
        // Distance to the shop from a user's point (calculated, nullable)
        public double? DistanceInMeters { get; set; }
    }
}
// // src/AutomotiveServices.Api/Dtos/ShopDto.cs
// using AutomotiveServices.Api.Models; // For Enums

// namespace AutomotiveServices.Api.Dtos;

// public class ShopDto
// {
//     public Guid Id { get; set; }
//     public string NameEn { get; set; } = string.Empty;
//     public string NameAr { get; set; } = string.Empty;
//     public string? Slug { get; set; } // Shop's own slug, if any
//     public string? LogoUrl { get; set; }

//     public string? DescriptionEn { get; set; }
//     public string? DescriptionAr { get; set; }
//     public string Address { get; set; } = string.Empty;
//     public double Latitude { get; set; }
//     public double Longitude { get; set; }
//     public string? PhoneNumber { get; set; }
//     public string? ServicesOffered { get; set; }
//     public string? OpeningHours { get; set; }
    
//     // SubCategory Information
//     public ShopCategory SubCategory { get; set; } // e.g., ShopCategory.OilChange
//     public string SubCategoryName => SubCategory.ToString(); // e.g., "OilChange"
//     public string SubCategorySlug => CategoryInfo.GetSlug(SubCategory); // e.g., "oil-change"
//     public Models.HighLevelConcept Concept => CategoryInfo.GetConcept(SubCategory);
//     public int CityId { get; set; }
//     public double? DistanceInMeters { get; set; }
// }
// // // Dtos/ShopDto.cs
// // using System.Collections.Generic;
// // using AutomotiveServices.Api.Models; // For List

// // namespace AutomotiveServices.Api.Dtos;

// // public class ShopDto
// // {
// //     public Guid Id { get; set; }
// //     public string NameEn { get; set; } = string.Empty;
// //     public string NameAr { get; set; } = string.Empty;
// //     public string? Slug { get; set; } // Shop's own slug, if any
// //     public string? LogoUrl { get; set; }

// //     public string? DescriptionEn { get; set; }
// //     public string? DescriptionAr { get; set; }
// //     public string Address { get; set; } = string.Empty;
// //     public double Latitude { get; set; }
// //     public double Longitude { get; set; }
// //     public string? PhoneNumber { get; set; }
// //     public string? ServicesOffered { get; set; } // Keep for detailed text
// //     public string? OpeningHours { get; set; }
    
// //    // SubCategory Information
// //     public ShopCategory SubCategory { get; set; } // e.g., ShopCategory.OilChange
// //     public string SubCategoryName => SubCategory.ToString(); // e.g., "OilChange"
// //     public string SubCategorySlug => CategoryInfo.GetSlug(SubCategory); // e.g., "oil-change"
// //     public HighLevelConcept Concept => (HighLevelConcept)CategoryInfo.GetConcept(SubCategory); // e.g., HighLevelConcept.Maintenance
    
// //     public Guid CityId { get; set; }
// //     // public string CityNameEn { get; set; } // Consider adding if frequently needed with shop details
// //     // public string CitySlug { get; set; } // Consider adding if frequently needed

// //     public double? DistanceInMeters { get; set; }
// // }
// // // // src/AutomotiveServices.Api/Dtos/ShopDto.cs
// // // namespace AutomotiveServices.Api.Dtos;

// // // public class ShopDto
// // // {
// // //     public Guid Id { get; set; }
// // //     public string NameEn { get; set; } = string.Empty;
// // //     public string NameAr { get; set; } = string.Empty;
// // //     public string? DescriptionEn { get; set; }
// // //     public string? DescriptionAr { get; set; }
// // //     public string Address { get; set; } = string.Empty;
// // //     public double Latitude { get; set; } // Will be populated from Location.Y
// // //     public double Longitude { get; set; } // Will be populated from Location.X
// // //     public string? PhoneNumber { get; set; }
// // //     public string? ServicesOffered { get; set; }
// // //     public string? OpeningHours { get; set; }
// // //     // CreatedAt and UpdatedAt removed
// // //     public double? DistanceInMeters { get; set; }
// // // }