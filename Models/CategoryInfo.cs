// src/AutomotiveServices.Api/Models/CategoryInfo.cs
using System.Collections.Generic; // For Dictionary, IEnumerable
using System.Linq;               // For LINQ methods

namespace AutomotiveServices.Api.Models; // Ensure this matches Enums.cs

public static class CategoryInfo
{
    // ShopCategory (SubCategory) to Slug mapping
    private static readonly Dictionary<ShopCategory, string> SubCategoryToSlugMap = new()
    {
        // Maintenance Subcategories
        { ShopCategory.GeneralMaintenance, "general-maintenance" },
        { ShopCategory.CarWash, "car-wash" },
        { ShopCategory.TireServices, "tire-services" },
        { ShopCategory.OilChange, "oil-change" },
        { ShopCategory.EVCharging, "ev-charging" },
        { ShopCategory.BodyRepairAndPaint, "body-repair-paint" },
        { ShopCategory.Diagnostics, "diagnostics" },
        { ShopCategory.Brakes, "brakes" },
        { ShopCategory.ACRepair, "ac-repair" },

        // Marketplace Subcategories (EXAMPLES - ALIGN WITH YOUR ShopCategory ENUM)
        { ShopCategory.NewAutoParts, "new-auto-parts" },
        { ShopCategory.UsedAutoParts, "used-auto-parts" },
        { ShopCategory.CarAccessories, "car-accessories" },
        { ShopCategory.PerformanceParts, "performance-parts" },

        { ShopCategory.Unknown, "unknown" } // Fallback
    };

    // Slug to ShopCategory (SubCategory) mapping
    private static readonly Dictionary<string, ShopCategory> SlugToSubCategoryMap =
        SubCategoryToSlugMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    // ShopCategory (SubCategory) to HighLevelConcept mapping
    // *** ENSURE HighLevelConcept here correctly refers to AutomotiveServices.Api.Models.HighLevelConcept ***
    private static readonly Dictionary<AutomotiveServices.Api.Models.ShopCategory, AutomotiveServices.Api.Models.HighLevelConcept> SubCategoryToConceptMap = new()
    {
        // Maintenance
        { ShopCategory.GeneralMaintenance, HighLevelConcept.Maintenance },
        { ShopCategory.CarWash, HighLevelConcept.Maintenance },
        { ShopCategory.TireServices, HighLevelConcept.Maintenance },
        { ShopCategory.OilChange, HighLevelConcept.Maintenance },
        { ShopCategory.EVCharging, HighLevelConcept.Maintenance },
        { ShopCategory.BodyRepairAndPaint, HighLevelConcept.Maintenance },
        { ShopCategory.Diagnostics, HighLevelConcept.Maintenance },
        { ShopCategory.Brakes, HighLevelConcept.Maintenance },
        { ShopCategory.ACRepair, HighLevelConcept.Maintenance },

        // Marketplace (EXAMPLES - ALIGN WITH YOUR ShopCategory ENUM)
        { ShopCategory.NewAutoParts, HighLevelConcept.Marketplace },
        { ShopCategory.UsedAutoParts, HighLevelConcept.Marketplace },
        { ShopCategory.CarAccessories, HighLevelConcept.Marketplace },
        { ShopCategory.PerformanceParts, HighLevelConcept.Marketplace },

        { ShopCategory.Unknown, HighLevelConcept.Unknown } // Fallback
    };

    public static string GetSlug(ShopCategory subCategory) =>
        SubCategoryToSlugMap.TryGetValue(subCategory, out var slug) ? slug : "unknown";

    public static ShopCategory GetSubCategory(string slug) =>
        SlugToSubCategoryMap.TryGetValue(slug?.ToLowerInvariant() ?? "", out var subCategory) ? subCategory : ShopCategory.Unknown;

    // *** ENSURE this return type HighLevelConcept correctly refers to AutomotiveServices.Api.Models.HighLevelConcept ***
    public static HighLevelConcept GetConcept(ShopCategory subCategory) =>
        SubCategoryToConceptMap.TryGetValue(subCategory, out var concept) ? concept : HighLevelConcept.Unknown;

    public static bool IsValidSubCategorySlug(string slug) =>
        SlugToSubCategoryMap.ContainsKey(slug?.ToLowerInvariant() ?? "");

    public static IEnumerable<(string Slug, string Name, ShopCategory CategoryEnum)> GetSubCategoriesForConcept(HighLevelConcept concept)
    {
        return SubCategoryToConceptMap
            .Where(kvp => kvp.Value == concept && kvp.Key != ShopCategory.Unknown)
            .Select(kvp => (
                Slug: GetSlug(kvp.Key),
                Name: kvp.Key.ToString(), 
                CategoryEnum: kvp.Key
            ));
    }
}
// // src/AutomotiveServices.Api/Models/CategoryInfo.cs
// namespace AutomotiveServices.Api.Models;

// public static class CategoryInfo
// {
//     // ShopCategory (SubCategory) to Slug mapping
//     private static readonly Dictionary<ShopCategory, string> SubCategoryToSlugMap = new()
//     {
//         // Maintenance Subcategories
//         { ShopCategory.GeneralMaintenance, "general-maintenance" },
//         { ShopCategory.CarWash, "car-wash" },
//         { ShopCategory.TireServices, "tire-services" },
//         { ShopCategory.OilChange, "oil-change" },
//         { ShopCategory.EVCharging, "ev-charging" },
//         { ShopCategory.BodyRepairAndPaint, "body-repair-paint" },
//         { ShopCategory.Diagnostics, "diagnostics" },
//         { ShopCategory.Brakes, "brakes" },
//         { ShopCategory.ACRepair, "ac-repair" },

//         // Marketplace Subcategories (EXAMPLES - ALIGN WITH YOUR ShopCategory ENUM)
//         { ShopCategory.NewAutoParts, "new-auto-parts" },
//         { ShopCategory.UsedAutoParts, "used-auto-parts" },
//         { ShopCategory.CarAccessories, "car-accessories" },
//         { ShopCategory.PerformanceParts, "performance-parts" },

//         { ShopCategory.Unknown, "unknown" } // Fallback
//     };

//     // Slug to ShopCategory (SubCategory) mapping
//     private static readonly Dictionary<string, ShopCategory> SlugToSubCategoryMap =
//         SubCategoryToSlugMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

//     // ShopCategory (SubCategory) to HighLevelConcept mapping
//     private static readonly Dictionary<ShopCategory, HighLevelConcept> SubCategoryToConceptMap = new()
//     {
//         // Maintenance
//         { ShopCategory.GeneralMaintenance, HighLevelConcept.Maintenance },
//         { ShopCategory.CarWash, HighLevelConcept.Maintenance },
//         { ShopCategory.TireServices, HighLevelConcept.Maintenance },
//         { ShopCategory.OilChange, HighLevelConcept.Maintenance },
//         { ShopCategory.EVCharging, HighLevelConcept.Maintenance },
//         { ShopCategory.BodyRepairAndPaint, HighLevelConcept.Maintenance },
//         { ShopCategory.Diagnostics, HighLevelConcept.Maintenance },
//         { ShopCategory.Brakes, HighLevelConcept.Maintenance },
//         { ShopCategory.ACRepair, HighLevelConcept.Maintenance },

//         // Marketplace (EXAMPLES - ALIGN WITH YOUR ShopCategory ENUM)
//         { ShopCategory.NewAutoParts, HighLevelConcept.Marketplace },
//         { ShopCategory.UsedAutoParts, HighLevelConcept.Marketplace },
//         { ShopCategory.CarAccessories, HighLevelConcept.Marketplace },
//         { ShopCategory.PerformanceParts, HighLevelConcept.Marketplace },

//         { ShopCategory.Unknown, HighLevelConcept.Unknown } // Fallback
//     };

//     public static string GetSlug(ShopCategory subCategory) =>
//         SubCategoryToSlugMap.TryGetValue(subCategory, out var slug) ? slug : "unknown";

//     public static ShopCategory GetSubCategory(string slug) =>
//         SlugToSubCategoryMap.TryGetValue(slug?.ToLowerInvariant() ?? "", out var subCategory) ? subCategory : ShopCategory.Unknown;

//     public static HighLevelConcept GetConcept(ShopCategory subCategory) =>
//         SubCategoryToConceptMap.TryGetValue(subCategory, out var concept) ? concept : HighLevelConcept.Unknown;

//     public static bool IsValidSubCategorySlug(string slug) =>
//         SlugToSubCategoryMap.ContainsKey(slug?.ToLowerInvariant() ?? "");

//     public static IEnumerable<(string Slug, string Name, ShopCategory CategoryEnum)> GetSubCategoriesForConcept(HighLevelConcept concept)
//     {
//         return SubCategoryToConceptMap
//             .Where(kvp => kvp.Value == concept && kvp.Key != ShopCategory.Unknown)
//             .Select(kvp => (
//                 Slug: GetSlug(kvp.Key),
//                 Name: kvp.Key.ToString(), // Or a more user-friendly name from another map
//                 CategoryEnum: kvp.Key
//             ));
//     }
// }