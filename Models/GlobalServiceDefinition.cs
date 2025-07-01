// src/AutomotiveServices.Api/Models/GlobalServiceDefinition.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class GlobalServiceDefinition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // If using int PK
    public int GlobalServiceId { get; set; }
    // Alternatively, use Guid:
    // public Guid GlobalServiceId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string ServiceCode { get; set; } = string.Empty; // e.g., "OIL_CHANGE_STD", "BRAKE_PAD_REPLACE_FRONT"

    [Required]
    [MaxLength(200)]
    public string DefaultNameEn { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DefaultNameAr { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? DefaultDescriptionEn { get; set; }

    [MaxLength(1000)]
    public string? DefaultDescriptionAr { get; set; }

    [MaxLength(500)]
    public string? DefaultIconUrl { get; set; } // URL to a generic icon

    public int? DefaultEstimatedDurationMinutes { get; set; }

    public bool IsGloballyActive { get; set; } = true;

    // Relates to ShopCategory for grouping/filtering global services
    public ShopCategory Category { get; set; } = ShopCategory.Unknown; 

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property: Shops might offer this global service
    public ICollection<ShopService> ShopServices { get; set; } = new List<ShopService>();
}