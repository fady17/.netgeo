// src/AutomotiveServices.Api/Models/UserCartItem.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class UserCartItem
{
    [Key]
    public Guid UserCartItemId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)] // Assuming UserId will be 'sub' claim (string, potentially a GUID)
    public string UserId { get; set; } = string.Empty; 
    // No direct navigation property to an ApplicationUser (from IDP) here,
    // as this API is a resource server and doesn't directly manage IDP user entities.
    // UserId is the link.

    [Required]
    public Guid ShopId { get; set; }
    // No navigation to Shop needed if primarily for cart display

    [Required]
    public Guid ShopServiceId { get; set; }
    // No navigation to ShopService needed

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
    
    [MaxLength(200)] // Match Shop.NameEn length
    public string? ShopNameSnapshotEn { get; set; }

    [MaxLength(200)] // Match Shop.NameAr length
    public string? ShopNameSnapshotAr { get; set; }
    
    [MaxLength(500)]
    public string? ServiceImageUrlSnapshot { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}