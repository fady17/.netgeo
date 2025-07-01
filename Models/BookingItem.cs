// src/AutomotiveServices.Api/Models/BookingItem.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class BookingItem
{
    [Key]
    public Guid BookingItemId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid BookingId { get; set; } // FK to Bookings table
    public Booking Booking { get; set; } = null!; // Navigation property

    [Required]
    public Guid ShopServiceId { get; set; } // FK to the actual ShopService offered by the shop
    // Optional: Navigation property to ShopService if needed for direct lookups,
    // but often snapshots are enough for historical booking items.
    // public ShopService ShopService { get; set; } = null!; 

    [Required]
    [MaxLength(200)]
    public string ServiceNameSnapshotEn { get; set; } = string.Empty; // Snapshot from ShopService.EffectiveNameEn

    [Required]
    [MaxLength(200)]
    public string ServiceNameSnapshotAr { get; set; } = string.Empty; // Snapshot from ShopService.EffectiveNameAr
    
    [MaxLength(200)] // Match Shop.NameEn length
    public string? ShopNameSnapshotEn { get; set; } // Denormalized from ShopService's Shop

    [MaxLength(200)] // Match Shop.NameAr length
    public string? ShopNameSnapshotAr { get; set; }

    [Required]
    public int Quantity { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal PriceAtBooking { get; set; } // Price per unit when booking was made

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineItemTotal => Quantity * PriceAtBooking; // Calculated, not stored unless for perf

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}