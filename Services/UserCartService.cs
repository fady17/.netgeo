// src/AutomotiveServices.Api/Services/UserCartService.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models; // For UserCartItem entity
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public class UserCartService : IUserCartService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserCartService> _logger;

    public UserCartService(AppDbContext context, ILogger<UserCartService> logger)
    {
        _context = context;
        _logger = logger;
    }

    private async Task<List<UserCartItemDto>> GetUserCartItemsDtoAsync(string userId)
    {
        return await _context.UserCartItems
            .AsNoTracking()
            .Where(ci => ci.UserId == userId)
            .OrderByDescending(ci => ci.AddedAtUtc)
            .Select(ci => new UserCartItemDto
            {
                UserCartItemId = ci.UserCartItemId,
                ShopId = ci.ShopId,
                ShopServiceId = ci.ShopServiceId,
                Quantity = ci.Quantity,
                ServiceNameEn = ci.ServiceNameSnapshotEn,
                ServiceNameAr = ci.ServiceNameSnapshotAr,
                PriceAtAddition = ci.PriceAtAddition,
                ShopNameSnapshotEn = ci.ShopNameSnapshotEn,
                ShopNameSnapshotAr = ci.ShopNameSnapshotAr,
                ServiceImageUrlSnapshot = ci.ServiceImageUrlSnapshot,
                AddedAtUtc = ci.AddedAtUtc, // Ensure DTO matches this or handles mapping
                UpdatedAtUtc = ci.UpdatedAtUtc
            })
            .ToListAsync();
    }

    public async Task<UserCartApiResponseDto> GetCartAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetCartAsync called with null or empty userId.");
            // This should ideally be caught before service call, but as a safeguard:
            return new UserCartApiResponseDto { UserId = userId, Items = new List<UserCartItemDto>() };
        }

        var cartItemsDto = await GetUserCartItemsDtoAsync(userId);
        var lastUpdate = DateTime.UtcNow; 

        if (cartItemsDto.Any())
        {
             lastUpdate = await _context.UserCartItems
                .Where(ci => ci.UserId == userId)
                .MaxAsync(ci => (DateTime?)ci.UpdatedAtUtc) ?? DateTime.UtcNow;
        }

        return new UserCartApiResponseDto
        {
            UserId = userId,
            Items = cartItemsDto,
            TotalItems = cartItemsDto.Sum(i => i.Quantity),
            TotalAmount = cartItemsDto.Sum(i => i.PriceAtAddition * i.Quantity),
            LastUpdatedAt = lastUpdate
        };
    }

    public async Task<UserCartApiResponseDto> AddItemAsync(string userId, AddToUserCartRequestDto itemDto)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
        if (itemDto == null) throw new ArgumentNullException(nameof(itemDto));

        var shopService = await _context.ShopServices
            .AsNoTracking()
            .Include(ss => ss.GlobalServiceDefinition) // For fallback names/images
            .FirstOrDefaultAsync(ss => ss.ShopId == itemDto.ShopId && ss.ShopServiceId == itemDto.ShopServiceId && ss.IsOfferedByShop);

        if (shopService == null)
        {
            _logger.LogWarning("AddItemAsync (User): ShopService not found or not offered. ShopServiceId: {ShopServiceId}, ShopId: {ShopId}, UserId: {UserId}",
                itemDto.ShopServiceId, itemDto.ShopId, userId);
            throw new KeyNotFoundException($"Service with ID {itemDto.ShopServiceId} not found or not currently offered by shop {itemDto.ShopId}.");
        }
        
        var shop = await _context.Shops
            .AsNoTracking()
            .Where(s => s.Id == itemDto.ShopId)
            .Select(s => new { s.NameEn, s.NameAr })
            .FirstOrDefaultAsync();

        if (shop == null) {
             _logger.LogError("AddItemAsync (User): Shop details not found for ShopId {ShopId} when adding to cart for UserId: {UserId}.", itemDto.ShopId, userId);
             throw new KeyNotFoundException($"Shop with ID {itemDto.ShopId} not found.");
        }


        var existingCartItem = await _context.UserCartItems
            .FirstOrDefaultAsync(ci => ci.UserId == userId &&
                                        ci.ShopId == itemDto.ShopId &&
                                        ci.ShopServiceId == itemDto.ShopServiceId);

        if (existingCartItem != null)
        {
            existingCartItem.Quantity += itemDto.Quantity;
            existingCartItem.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var newCartItem = new UserCartItem
            {
                UserId = userId,
                ShopId = itemDto.ShopId,
                ShopServiceId = itemDto.ShopServiceId,
                Quantity = itemDto.Quantity,
                PriceAtAddition = shopService.Price,
                ServiceNameSnapshotEn = shopService.EffectiveNameEn,
                ServiceNameSnapshotAr = shopService.EffectiveNameAr,
                ShopNameSnapshotEn = shop.NameEn,
                ShopNameSnapshotAr = shop.NameAr,
                ServiceImageUrlSnapshot = shopService.ShopSpecificIconUrl ?? shopService.GlobalServiceDefinition?.DefaultIconUrl,
                AddedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.UserCartItems.Add(newCartItem);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Item ShopServiceId:{ShopServiceId} (Qty: {Quantity}) added/updated in user cart for UserId: {UserId}.",
            itemDto.ShopServiceId, itemDto.Quantity, userId);

        return await GetCartAsync(userId);
    }

    public async Task<UserCartApiResponseDto> UpdateItemAsync(string userId, Guid userCartItemId, int newQuantity)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));

        var cartItem = await _context.UserCartItems
            .FirstOrDefaultAsync(ci => ci.UserId == userId && ci.UserCartItemId == userCartItemId);

        if (cartItem == null)
        {
            _logger.LogWarning("UpdateItemAsync (User): Cart item not found. UserCartItemId: {UserCartItemId}, UserId: {UserId}",
                userCartItemId, userId);
            throw new KeyNotFoundException($"Cart item with ID {userCartItemId} not found for user {userId}.");
        }

        if (newQuantity <= 0)
        {
            _context.UserCartItems.Remove(cartItem);
            _logger.LogInformation("Item UserCartItemId:{UserCartItemId} removed due to quantity <= 0 for UserId: {UserId}.",
                userCartItemId, userId);
        }
        else
        {
            cartItem.Quantity = newQuantity;
            cartItem.UpdatedAtUtc = DateTime.UtcNow;
            _logger.LogInformation("Item UserCartItemId:{UserCartItemId} quantity updated to {NewQuantity} for UserId: {UserId}.",
                userCartItemId, newQuantity, userId);
        }

        await _context.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task<UserCartApiResponseDto> RemoveItemAsync(string userId, Guid userCartItemId)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));

        var cartItem = await _context.UserCartItems
            .FirstOrDefaultAsync(ci => ci.UserId == userId && ci.UserCartItemId == userCartItemId);

        if (cartItem == null)
        {
            _logger.LogWarning("RemoveItemAsync (User): Cart item not found for removal. UserCartItemId: {UserCartItemId}, UserId: {UserId}",
                userCartItemId, userId);
            throw new KeyNotFoundException($"Cart item with ID {userCartItemId} not found for user {userId} to remove.");
        }

        _context.UserCartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Item UserCartItemId:{UserCartItemId} removed from cart for UserId: {UserId}.",
            userCartItemId, userId);

        return await GetCartAsync(userId);
    }

    public async Task ClearCartAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));

        var cartItems = await _context.UserCartItems
            .Where(ci => ci.UserId == userId)
            .ToListAsync();

        if (cartItems.Any())
        {
            _context.UserCartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cart cleared for UserId: {UserId}. Removed {ItemCount} items.",
                userId, cartItems.Count);
        }
        else
        {
            _logger.LogInformation("Attempted to clear cart for UserId: {UserId}, but cart was already empty.", userId);
        }
    }
}