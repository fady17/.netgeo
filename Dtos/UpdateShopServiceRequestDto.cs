// src/AutomotiveServices.Api/Dtos/UpdateShopServiceRequestDto.cs
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

public class UpdateShopServiceRequestDto
{
    // Note: ShopId and ShopServiceId will typically come from the route or claims, not the body.
    // GlobalServiceId is generally not updatable once linked, but name/desc/price can be.

    [MaxLength(200)]
    public string? CustomServiceNameEn { get; set; } // If they want to set/change custom name

    [MaxLength(200)]
    public string? CustomServiceNameAr { get; set; }

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

    [Required]
    public bool IsOfferedByShop { get; set; }

    public int SortOrder { get; set; }
    public bool IsPopularAtShop { get; set; }
}