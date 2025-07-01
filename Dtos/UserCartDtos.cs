// src/AutomotiveServices.Api/Dtos/UserCartDtos.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Dtos;

/// <summary>
/// Represents an item in an authenticated user's cart.
/// Similar to AnonymousCartItemDto but contextually for a logged-in user.
/// </summary>
public class UserCartItemDto
{
    public Guid UserCartItemId { get; set; }
    public Guid ShopId { get; set; }
    public Guid ShopServiceId { get; set; }
    public int Quantity { get; set; }
    public string ServiceNameEn { get; set; } = string.Empty;
    public string ServiceNameAr { get; set; } = string.Empty;
    public decimal PriceAtAddition { get; set; }
    public string? ShopNameSnapshotEn { get; set; }
    public string? ShopNameSnapshotAr { get; set; }
    public string? ServiceImageUrlSnapshot { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Request DTO for adding an item to an authenticated user's cart.
/// </summary>
public class AddToUserCartRequestDto
{
    [Required]
    public Guid ShopId { get; set; }
    [Required]
    public Guid ShopServiceId { get; set; }
    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")]
    public int Quantity { get; set; }
}

/// <summary>
/// Request DTO for updating an item's quantity in an authenticated user's cart.
/// </summary>
public class UpdateUserCartItemQuantityRequestDto
{
    [Range(0, 100, ErrorMessage = "Quantity must be between 0 and 100. Use 0 to remove.")]
    public int NewQuantity { get; set; }
}

/// <summary>
/// API response for authenticated user's cart operations.
/// </summary>
public class UserCartApiResponseDto
{
    public string UserId { get; set; } = string.Empty; // The authenticated user's ID
    public List<UserCartItemDto> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    // public string? CurrencyCode { get; set; } = "EGP";
}