// src/AutomotiveServices.Api/Models/AnonymousCartItem.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class AnonymousCartItem
{
    [Key]
    public Guid AnonymousCartItemId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)] // Assuming anon_id (UUID string) fits
    public string AnonymousUserId { get; set; } = string.Empty; // Stores the 'anon_id' claim

    [Required]
    public Guid ShopId { get; set; }
    // No navigation property to Shop needed here if not directly queried together often

    [Required]
    public Guid ShopServiceId { get; set; }
    // No navigation property to ShopService needed for the same reason

    [Required]
    public int Quantity { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal PriceAtAddition { get; set; }

    [Required]
    [MaxLength(200)]
    public string ServiceNameSnapshotEn { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string ServiceNameSnapshotAr { get; set; } = string.Empty;
    
    // --- NEW PROPERTIES for Shop Name Snapshot ---
    [MaxLength(200)] // Match Shop.NameEn length
    public string? ShopNameSnapshotEn { get; set; } // Nullable, in case shop name somehow isn't found

    [MaxLength(200)] // Match Shop.NameAr length
    public string? ShopNameSnapshotAr { get; set; }
    // --- END NEW PROPERTIES ---
    
    [MaxLength(500)]
    public string? ServiceImageUrlSnapshot { get; set; } // Optional

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow; 
}
// // src/AutomotiveServices.Api/Models/AnonymousCartItem.cs
// using System;
// using System.ComponentModel.DataAnnotations;
// using System.ComponentModel.DataAnnotations.Schema;

// namespace AutomotiveServices.Api.Models;

// public class AnonymousCartItem
// {
//     [Key]
//     public Guid AnonymousCartItemId { get; set; } = Guid.NewGuid();

//     [Required]
//     [MaxLength(100)] // Assuming anon_id (UUID string) fits
//     public string AnonymousUserId { get; set; } = string.Empty; // Stores the 'anon_id' claim

//     [Required]
//     public Guid ShopId { get; set; }
//     // No navigation property to Shop needed here if not directly queried together often

//     [Required]
//     public Guid ShopServiceId { get; set; }
//     // No navigation property to ShopService needed for the same reason

//     [Required]
//     public int Quantity { get; set; }

//     [Required]
//     [Column(TypeName = "decimal(18,2)")]
//     public decimal PriceAtAddition { get; set; }

//     [Required]
//     [MaxLength(200)]
//     public string ServiceNameSnapshotEn { get; set; } = string.Empty;

//     [Required]
//     [MaxLength(200)]
//     public string ServiceNameSnapshotAr { get; set; } = string.Empty;
    
//     // Optional: if you want to store a denormalized image URL for the service/shop
//     [MaxLength(500)]
//     public string? ServiceImageUrlSnapshot { get; set; }


//     public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
//     public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow; 
// }