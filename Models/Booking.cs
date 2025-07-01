// src/AutomotiveServices.Api/Models/Booking.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public enum BookingStatus
{
    PendingConfirmation = 0, // Initial state after user places "cash" booking
    ConfirmedByShop = 1,     // Shop has confirmed the appointment/service
    InProgress = 2,          // Service is currently being performed
    Completed = 3,
    CancelledByUser = 4,
    CancelledByShop = 5,
    PaymentPending = 6,      // If cash on delivery/service, this might be pre-completion
    PaymentReceived = 7
}

public class Booking
{
    [Key]
    public Guid BookingId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)] // To store the authenticated UserId ('sub' claim)
    public string UserId { get; set; } = string.Empty;

    // If a booking is always tied to a single shop from which all services were ordered.
    // If a "booking" can span multiple shops (less common for initial implementation), this changes.
    // Assuming single shop per booking for now.
    [Required]
    public Guid ShopId { get; set; }
    public Shop Shop { get; set; } = null!; // Navigation to the shop

    [Required]
    public BookingStatus Status { get; set; } = BookingStatus.PendingConfirmation;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmountAtBooking { get; set; } // Total price when booking was made

    [MaxLength(1000)]
    public string? PreferredDateTimeNotes { get; set; } // User notes on preferred time/date

    [MaxLength(500)]
    public string? UserContactPhoneNumber { get; set; } // Snapshot or confirmed phone

    [MaxLength(255)]
    public string? UserContactEmail { get; set; } // Snapshot or confirmed email

    // Could also add vehicle details if relevant per booking
    // public Guid? VehicleId { get; set; }
    // public string? VehicleMakeModelSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    // Navigation property for booking items
    public ICollection<BookingItem> BookingItems { get; set; } = new List<BookingItem>();
}