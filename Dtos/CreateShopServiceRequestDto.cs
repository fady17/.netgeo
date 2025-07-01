// src/AutomotiveServices.Api/Dtos/CreateShopServiceRequestDto.cs
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

public class CreateShopServiceRequestDto
{
    // Option 1: Link to an existing global service
    public int? GlobalServiceId { get; set; }

    // Option 2: Define a custom service (required if GlobalServiceId is null)
    // Or, use these to override global definition properties.
    [MaxLength(200)]
    public string? CustomServiceNameEn { get; set; } // Required if GlobalServiceId is null

    [MaxLength(200)]
    public string? CustomServiceNameAr { get; set; } // Required if GlobalServiceId is null

    [MaxLength(1000)]
    public string? ShopSpecificDescriptionEn { get; set; }

    [MaxLength(1000)]
    public string? ShopSpecificDescriptionAr { get; set; }

    [Required]
    [Range(0.01, (double)decimal.MaxValue, ErrorMessage = "Price must be greater than 0.")]
    public decimal Price { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Duration must be a positive number.")]
    public int? DurationMinutes { get; set; }

    [MaxLength(500)]
    public string? ShopSpecificIconUrl { get; set; }

    public bool IsOfferedByShop { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public bool IsPopularAtShop { get; set; } = false;

    // Validation logic would ensure either GlobalServiceId is provided OR
    // CustomServiceNameEn/Ar are provided.
}