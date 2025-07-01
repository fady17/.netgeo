// src/AutomotiveServices.Api/Models/ShopService.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class ShopService
{
    [Key]
    public Guid ShopServiceId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid     ShopId { get; set; } // FK to Shops table
    public Shop Shop { get; set; } = null!; // Navigation property

    // If this service is based on a global definition
    public int? GlobalServiceId { get; set; } // FK to GlobalServiceDefinitions, nullable
    public GlobalServiceDefinition? GlobalServiceDefinition { get; set; } // Navigation property

    // If GlobalServiceId is null, these names are required.
    // If GlobalServiceId is not null, these can override the global definition's names.
    [MaxLength(200)]
    public string? CustomServiceNameEn { get; set; }

    [MaxLength(200)]
    public string? CustomServiceNameAr { get; set; }
    
    // For easier querying/display, store the name that should be used.
    // This could be populated by logic before saving (NotMapped if purely computed in C#).
    // Or, make it a regular mapped property updated on save. Let's map it for now.
    [Required]
    [MaxLength(200)]
    public string EffectiveNameEn { get; set; } = string.Empty; 

    [Required]
    [MaxLength(200)]
    public string EffectiveNameAr { get; set; } = string.Empty;


    [MaxLength(1000)]
    public string? ShopSpecificDescriptionEn { get; set; } // Overrides global if present

    [MaxLength(1000)]
    public string? ShopSpecificDescriptionAr { get; set; }

    [Column(TypeName = "decimal(18,2)")] // Appropriate for currency
    [Required]
    public decimal Price { get; set; }

    public int? DurationMinutes { get; set; } // Shop's estimate, overrides global

    [MaxLength(500)]
    public string? ShopSpecificIconUrl { get; set; } // Overrides global

    public bool IsOfferedByShop { get; set; } = true; // Shop can toggle this

    public int SortOrder { get; set; } = 0; // For display order within shop's services

    public bool IsPopularAtShop { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}