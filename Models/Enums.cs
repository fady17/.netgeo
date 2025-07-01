// src/AutomotiveServices.Api/Models/Enums.cs
namespace AutomotiveServices.Api.Models;

public enum ShopCategory
{
    // Maintenance Subcategories
    Unknown = 0, // General fallback
    GeneralMaintenance = 1,
    CarWash = 2,
    TireServices = 3,
    OilChange = 4,
    EVCharging = 5,
    BodyRepairAndPaint = 6,
    Diagnostics = 7,
    Brakes = 8,
    ACRepair = 9,

    // Marketplace Subcategories (EXAMPLES - DEFINE YOUR ACTUAL VALUES)
    NewAutoParts = 101,
    UsedAutoParts = 102,
    CarAccessories = 103,
    PerformanceParts = 104
}

public enum HighLevelConcept
{
    Unknown = 0,
    Maintenance = 1,
    Marketplace = 2
}