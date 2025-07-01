// src/AutomotiveServices.Api/Dtos/BookingDtos.cs
using AutomotiveServices.Api.Models; // For BookingStatus enum
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

/// <summary>
/// Represents a line item within a booking.
/// </summary>
public class BookingItemDto
{
    public Guid BookingItemId { get; set; }
    public Guid ShopServiceId { get; set; }
    public string ServiceNameSnapshotEn { get; set; } = string.Empty;
    public string ServiceNameSnapshotAr { get; set; } = string.Empty;
    public string? ShopNameSnapshotEn { get; set; } // Denormalized shop name
    public string? ShopNameSnapshotAr { get; set; }
    public int Quantity { get; set; }
    public decimal PriceAtBooking { get; set; }
    public decimal LineItemTotal => Quantity * PriceAtBooking;
}

/// <summary>
/// DTO for creating a new booking from the user's cart.
/// </summary>
public class CreateBookingRequestDto
{
    // UserId will be taken from the authenticated user's claims.
    // ShopId will be inferred if cart items are from a single shop,
    // or client might need to specify if creating booking per shop.
    // For now, assume service layer handles grouping cart items by shop.

    [MaxLength(1000)]
    public string? PreferredDateTimeNotes { get; set; }

    [Phone(ErrorMessage = "Invalid phone number format.")]
    [MaxLength(20)]
    public string? UserContactPhoneNumber { get; set; } // Optional override/confirmation

    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [MaxLength(255)]
    public string? UserContactEmail { get; set; } // Optional override/confirmation
    
    // If a booking needs to be explicitly for one shop from the cart
    // public Guid? TargetShopId { get; set; }
}

/// <summary>
/// DTO representing a booking, returned by the API.
/// </summary>
public class BookingDto // Renamed from BookingResponseDto for common use
{
    public Guid BookingId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ShopId { get; set; }
    public string ShopNameEn { get; set; } = string.Empty; // Denormalized for convenience
    public string ShopNameAr { get; set; } = string.Empty;
    public BookingStatus Status { get; set; }
    public string StatusText => Status.ToString(); // User-friendly status text
    public decimal TotalAmountAtBooking { get; set; }
    public string? PreferredDateTimeNotes { get; set; }
    public string? UserContactPhoneNumber { get; set; }
    public string? UserContactEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public List<BookingItemDto> Items { get; set; } = new();
}