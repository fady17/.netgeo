// src/AutomotiveServices.Api/Dtos/AnonymousCartDtos.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // Keep for AddToAnonymousCartRequestDto

namespace AutomotiveServices.Api.Dtos;

public class AnonymousCartItemDto
{
    public Guid AnonymousCartItemId { get; set; }
    public Guid ShopId { get; set; }
    public Guid ShopServiceId { get; set; }
    public int Quantity { get; set; }
    public string ServiceNameEn { get; set; } = string.Empty;
    public string ServiceNameAr { get; set; } = string.Empty;
    public decimal PriceAtAddition { get; set; }
    public string? ServiceImageUrlSnapshot { get; set; }
    public DateTime AddedAt { get; set; } // Should this be AddedAtUtc from entity? Yes, consistent naming.

    // --- NEW PROPERTIES to match entity ---
    public string? ShopNameSnapshotEn { get; set; }
    public string? ShopNameSnapshotAr { get; set; }
    // --- END NEW PROPERTIES ---
}

public class AddToAnonymousCartRequestDto
{
    [Required]
    public Guid ShopId { get; set; }
    [Required]
    public Guid ShopServiceId { get; set; }
    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")] // Max quantity example
    public int Quantity { get; set; }
}

// This DTO is still fine, no changes needed from previous model update
// public class UpdateCartItemQuantityRequestDto
// {
//     [Range(0, 100, ErrorMessage = "Quantity must be between 0 and 100.")]
//     public int NewQuantity { get; set; }
// }

public class AnonymousCartApiResponseDto
{
    public string AnonymousUserId { get; set; } = string.Empty;
    public List<AnonymousCartItemDto> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime LastUpdatedAt { get; set; } // This should ideally reflect the latest cart modification
    // public string? CurrencyCode { get; set; } = "EGP"; // If needed
}

public class MergeAnonymousDataResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public MergeDetails? Details { get; set; } // Make Details nullable

    // Nested class for merge statistics
    public class MergeDetails
    {
        public int CartItemsTransferred { get; set; }
        public int DuplicatesHandled { get; set; } // e.g., items where quantity was updated
        public bool PreferencesTransferred { get; set; }
        // Add more details if needed
    }
}
// Also ensure UpdateAnonymousLocationRequestDto is defined:
public class UpdateAnonymousLocationRequestDto
{
    [Range(-90.0, 90.0)]
    public double Latitude { get; set; }
    [Range(-180.0, 180.0)]
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
    [Required]
    public string Source { get; set; } = string.Empty;


}

// And AnonymousUserPreferenceDto
public class AnonymousUserPreferenceDto
{
    public double? LastKnownLatitude { get; set; }
    public double? LastKnownLongitude { get; set; }
    public double? LastKnownLocationAccuracy { get; set; }
    public string? LocationSource { get; set; }
    public DateTime LastSetAtUtc { get; set; }
}
// // src/AutomotiveServices.Api/Dtos/AnonymousCartDtos.cs
// using System;
// using System.Collections.Generic;
// using System.ComponentModel.DataAnnotations;

// namespace AutomotiveServices.Api.Dtos; // Or AutomotiveServices.Api.Dtos.Anonymous

// public class AnonymousCartItemDto
// {
//     public Guid AnonymousCartItemId { get; set; } // Useful for client to identify item for update/delete
//     public Guid ShopId { get; set; }
//     public Guid ShopServiceId { get; set; }
//     public int Quantity { get; set; }
//     public string ServiceNameEn { get; set; } = string.Empty;
//     public string ServiceNameAr { get; set; } = string.Empty;
//     public decimal PriceAtAddition { get; set; }
//     public string? ServiceImageUrlSnapshot { get; set; }
//     public DateTime AddedAt { get; set; }
//     // You might also add shopNameEn/Ar if the service needs to populate this for the DTO
// }

// public class AddToAnonymousCartRequestDto
// {
//     [Required]
//     public Guid ShopId { get; set; }
//     [Required]
//     public Guid ShopServiceId { get; set; }
//     [Range(1, 100)] // Max quantity example
//     public int Quantity { get; set; }
// }


// public class AnonymousCartApiResponseDto
// {
//     public string AnonymousUserId { get; set; } = string.Empty;
//     public List<AnonymousCartItemDto> Items { get; set; } = new();
//     public int TotalItems { get; set; }
//     public decimal TotalAmount { get; set; }
//     public DateTime LastUpdatedAt { get; set; }
//     // public string? CurrencyCode { get; set; } = "EGP"; // If needed
// }