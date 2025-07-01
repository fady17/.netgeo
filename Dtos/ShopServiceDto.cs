// src/AutomotiveServices.Api/Dtos/ShopServiceDto.cs
namespace AutomotiveServices.Api.Dtos;

public class ShopServiceDto
{
    public Guid ShopServiceId { get; set; }
    public Guid ShopId { get; set; }

    // Information about the service itself
    public string NameEn { get; set; } = string.Empty; // This will be EffectiveNameEn
    public string NameAr { get; set; } = string.Empty; // This will be EffectiveNameAr
    public string? DescriptionEn { get; set; } // Shop-specific or fallback to global
    public string? DescriptionAr { get; set; } // Shop-specific or fallback to global
    public decimal Price { get; set; }
    public int? DurationMinutes { get; set; } // Shop-specific or fallback to global
    public string? IconUrl { get; set; } // Shop-specific or fallback to global

    public bool IsPopularAtShop { get; set; }
    public int SortOrder { get; set; }

    // Optional: If linked to a global service, you might want to include its ID or code
    public int? GlobalServiceId { get; set; }
    public string? GlobalServiceCode { get; set; } // If GlobalServiceId is not null
}