// src/AutomotiveServices.Api/Services/AnonymousCartService.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AutomotiveServices.Api.Services;

public class AnonymousCartService : IAnonymousCartService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AnonymousCartService> _logger;

    public AnonymousCartService(AppDbContext context, ILogger<AnonymousCartService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Helper to get DTOs, now includes mapping ShopNameSnapshots
    private async Task<List<AnonymousCartItemDto>> GetCartItemsDtoAsync(string anonymousUserId)
    {
        return await _context.AnonymousCartItems
            .AsNoTracking()
            .Where(ci => ci.AnonymousUserId == anonymousUserId)
            .OrderByDescending(ci => ci.AddedAtUtc) // Order by when it was added
            .Select(ci => new AnonymousCartItemDto
            {
                AnonymousCartItemId = ci.AnonymousCartItemId,
                ShopId = ci.ShopId,
                ShopServiceId = ci.ShopServiceId,
                Quantity = ci.Quantity,
                ServiceNameEn = ci.ServiceNameSnapshotEn,
                ServiceNameAr = ci.ServiceNameSnapshotAr,
                PriceAtAddition = ci.PriceAtAddition,
                ServiceImageUrlSnapshot = ci.ServiceImageUrlSnapshot,
                AddedAt = ci.AddedAtUtc, // Map UTC to DTO. Client can format.
                // --- MAP NEW SHOP NAME SNAPSHOTS ---
                ShopNameSnapshotEn = ci.ShopNameSnapshotEn,
                ShopNameSnapshotAr = ci.ShopNameSnapshotAr
                // --- END MAP NEW ---
            })
            .ToListAsync();
    }

    public async Task<AnonymousCartApiResponseDto> GetCartAsync(string anonymousUserId)
    {
        var cartItemsDto = await GetCartItemsDtoAsync(anonymousUserId);
        var lastUpdate = DateTime.UtcNow; // Default to now if no items

        if (cartItemsDto.Any())
        {
            // Determine last update by looking at the most recently updated item in the DB
            // This requires AnonymousCartItem to have UpdatedAtUtc consistently set.
            // For simplicity, if DTO only has AddedAt, we use that.
            // If AnonymousCartItem entity has UpdatedAtUtc, then GetCartItemsDtoAsync should map it too.
            // For now, let's assume the items in DTO have AddedAt.
             lastUpdate = await _context.AnonymousCartItems
                .Where(ci => ci.AnonymousUserId == anonymousUserId)
                .MaxAsync(ci => (DateTime?)ci.UpdatedAtUtc) ?? DateTime.UtcNow;
        }


        return new AnonymousCartApiResponseDto
        {
            AnonymousUserId = anonymousUserId,
            Items = cartItemsDto,
            TotalItems = cartItemsDto.Sum(i => i.Quantity),
            TotalAmount = cartItemsDto.Sum(i => i.PriceAtAddition * i.Quantity),
            LastUpdatedAt = lastUpdate
        };
    }


    public async Task<AnonymousCartApiResponseDto> AddItemAsync(string anonymousUserId, AddToAnonymousCartRequestDto itemDto)
    {
        // 1. Fetch the ShopService to get its current details
        var shopService = await _context.ShopServices
            .AsNoTracking()
            .Include(ss => ss.GlobalServiceDefinition)
            .FirstOrDefaultAsync(ss => ss.ShopId == itemDto.ShopId && ss.ShopServiceId == itemDto.ShopServiceId && ss.IsOfferedByShop);

        if (shopService == null)
        {
            _logger.LogWarning("AddItemAsync: ShopService not found or not offered. ShopServiceId: {ShopServiceId}, ShopId: {ShopId}, AnonymousUserId: {AnonymousUserId}",
                itemDto.ShopServiceId, itemDto.ShopId, anonymousUserId);
            throw new KeyNotFoundException($"Service with ID {itemDto.ShopServiceId} not found or not currently offered by shop {itemDto.ShopId}.");
        }

        // --- NEW: Fetch Shop Name for Snapshot ---
        var shop = await _context.Shops
            .AsNoTracking()
            .Where(s => s.Id == itemDto.ShopId)
            .Select(s => new { s.NameEn, s.NameAr }) // Select only needed fields
            .FirstOrDefaultAsync();

        if (shop == null)
        {
            // This should be rare if shopService was found, but as a safeguard
            _logger.LogError("AddItemAsync: Shop details not found for ShopId {ShopId} when adding to cart for AnonymousUserId: {AnonymousUserId}. This indicates a data integrity issue.",
                itemDto.ShopId, anonymousUserId);
            // Depending on policy, you might throw, or proceed with null shop names
            // Throwing is safer to highlight data issues.
            throw new KeyNotFoundException($"Shop with ID {itemDto.ShopId} not found, cannot add service to cart.");
        }
        // --- END NEW ---

        // 2. Check if item already exists in anonymous cart for this user & service
        var existingCartItem = await _context.AnonymousCartItems
            .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId &&
                                        ci.ShopId == itemDto.ShopId &&
                                        ci.ShopServiceId == itemDto.ShopServiceId);

        if (existingCartItem != null)
        {
            existingCartItem.Quantity += itemDto.Quantity;
            existingCartItem.UpdatedAtUtc = DateTime.UtcNow;
            // ShopNameSnapshot and ServiceNameSnapshot, PriceAtAddition are NOT updated here. They reflect the state when first added.
        }
        else
        {
            var newCartItem = new AnonymousCartItem
            {
                AnonymousUserId = anonymousUserId,
                ShopId = itemDto.ShopId,
                ShopServiceId = itemDto.ShopServiceId,
                Quantity = itemDto.Quantity,
                PriceAtAddition = shopService.Price,
                ServiceNameSnapshotEn = shopService.EffectiveNameEn,
                ServiceNameSnapshotAr = shopService.EffectiveNameAr,
                // --- POPULATE NEW SHOP NAME SNAPSHOTS ---
                ShopNameSnapshotEn = shop.NameEn,
                ShopNameSnapshotAr = shop.NameAr,
                // --- END POPULATE ---
                ServiceImageUrlSnapshot = shopService.ShopSpecificIconUrl ?? shopService.GlobalServiceDefinition?.DefaultIconUrl,
                AddedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.AnonymousCartItems.Add(newCartItem);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Item ShopServiceId:{ShopServiceId} (Qty: {Quantity}) added/updated in cart for AnonymousUserId: {AnonymousUserId}.",
            itemDto.ShopServiceId, itemDto.Quantity, anonymousUserId);

        return await GetCartAsync(anonymousUserId);
    }

    public async Task<AnonymousCartApiResponseDto> UpdateItemAsync(string anonymousUserId, Guid anonymousCartItemId, int newQuantity)
    {
        var cartItem = await _context.AnonymousCartItems
            .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId && ci.AnonymousCartItemId == anonymousCartItemId);

        if (cartItem == null)
        {
            _logger.LogWarning("UpdateItemAsync: Cart item not found. AnonymousCartItemId: {AnonymousCartItemId}, AnonymousUserId: {AnonymousUserId}",
                anonymousCartItemId, anonymousUserId);
            throw new KeyNotFoundException($"Cart item with ID {anonymousCartItemId} not found for this anonymous user.");
        }

        if (newQuantity <= 0)
        {
            _context.AnonymousCartItems.Remove(cartItem);
            _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} removed due to quantity <= 0 for AnonymousUserId: {AnonymousUserId}.",
                anonymousCartItemId, anonymousUserId);
        }
        else
        {
            cartItem.Quantity = newQuantity;
            cartItem.UpdatedAtUtc = DateTime.UtcNow;
            _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} quantity updated to {NewQuantity} for AnonymousUserId: {AnonymousUserId}.",
                anonymousCartItemId, newQuantity, anonymousUserId);
        }

        await _context.SaveChangesAsync();
        return await GetCartAsync(anonymousUserId);
    }

    public async Task<AnonymousCartApiResponseDto> RemoveItemAsync(string anonymousUserId, Guid anonymousCartItemId)
    {
        var cartItem = await _context.AnonymousCartItems
            .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId && ci.AnonymousCartItemId == anonymousCartItemId);

        if (cartItem == null)
        {
            _logger.LogWarning("RemoveItemAsync: Cart item not found for removal. AnonymousCartItemId: {AnonymousCartItemId}, AnonymousUserId: {AnonymousUserId}",
                anonymousCartItemId, anonymousUserId);
            throw new KeyNotFoundException($"Cart item with ID {anonymousCartItemId} not found for this anonymous user to remove.");
        }

        _context.AnonymousCartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} removed from cart for AnonymousUserId: {AnonymousUserId}.",
            anonymousCartItemId, anonymousUserId);

        return await GetCartAsync(anonymousUserId);
    }

    public async Task ClearCartAsync(string anonymousUserId)
    {
        var cartItems = await _context.AnonymousCartItems
            .Where(ci => ci.AnonymousUserId == anonymousUserId)
            .ToListAsync(); // Materialize the list before removing

        if (cartItems.Any())
        {
            _context.AnonymousCartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cart cleared for AnonymousUserId: {AnonymousUserId}. Removed {ItemCount} items.",
                anonymousUserId, cartItems.Count);
        }
        else
        {
            _logger.LogInformation("Attempted to clear cart for AnonymousUserId: {AnonymousUserId}, but cart was already empty.", anonymousUserId);
        }
    }
}
// // src/AutomotiveServices.Api/Services/AnonymousCartService.cs
// using AutomotiveServices.Api.Data;
// using AutomotiveServices.Api.Dtos;
// using AutomotiveServices.Api.Models;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using System;
// using System.Linq;
// using System.Threading.Tasks;
// using System.Collections.Generic;

// namespace AutomotiveServices.Api.Services;

// public class AnonymousCartService : IAnonymousCartService
// {
//     private readonly AppDbContext _context;
//     private readonly ILogger<AnonymousCartService> _logger;

//     public AnonymousCartService(AppDbContext context, ILogger<AnonymousCartService> logger)
//     {
//         _context = context;
//         _logger = logger;
//     }

//     private async Task<List<AnonymousCartItemDto>> GetCartItemsDtoAsync(string anonymousUserId)
//     {
//         return await _context.AnonymousCartItems
//             .AsNoTracking()
//             .Where(ci => ci.AnonymousUserId == anonymousUserId)
//             .OrderByDescending(ci => ci.AddedAtUtc)
//             .Select(ci => new AnonymousCartItemDto
//             {
//                 AnonymousCartItemId = ci.AnonymousCartItemId,
//                 ShopId = ci.ShopId,
//                 ShopServiceId = ci.ShopServiceId,
//                 Quantity = ci.Quantity,
//                 ServiceNameEn = ci.ServiceNameSnapshotEn,
//                 ServiceNameAr = ci.ServiceNameSnapshotAr,
//                 PriceAtAddition = ci.PriceAtAddition,
//                 ServiceImageUrlSnapshot = ci.ServiceImageUrlSnapshot,
//                 AddedAt = ci.AddedAtUtc
//             })
//             .ToListAsync();
//     }

//     public async Task<AnonymousCartApiResponseDto> GetCartAsync(string anonymousUserId)
//     {
//         var cartItemsDto = await GetCartItemsDtoAsync(anonymousUserId);

//         return new AnonymousCartApiResponseDto
//         {
//             AnonymousUserId = anonymousUserId,
//             Items = cartItemsDto,
//             TotalItems = cartItemsDto.Sum(i => i.Quantity),
//             TotalAmount = cartItemsDto.Sum(i => i.PriceAtAddition * i.Quantity),
//             // Use UpdatedAtUtc from individual items if available, or AddedAtUtc
//             LastUpdatedAt = cartItemsDto.Any() ? cartItemsDto.Max(i => GetItemLastModified(i)) : DateTime.UtcNow
//         };
//     }
//     // Helper for LastUpdatedAt
//     private DateTime GetItemLastModified(AnonymousCartItemDto item)
//     {
//         // Assuming AnonymousCartItem entity will have UpdatedAtUtc.
//         // For DTO, we only have AddedAt for now.
//         // If cart entity itself had a LastModified, that would be better.
//         return item.AddedAt; 
//     }


//     public async Task<AnonymousCartApiResponseDto> AddItemAsync(string anonymousUserId, AddToAnonymousCartRequestDto itemDto)
//     {
//         var shopService = await _context.ShopServices
//             .AsNoTracking()
//             .Include(ss => ss.GlobalServiceDefinition)
//             .FirstOrDefaultAsync(ss => ss.ShopId == itemDto.ShopId && ss.ShopServiceId == itemDto.ShopServiceId && ss.IsOfferedByShop);

//         if (shopService == null)
//         {
//             _logger.LogWarning("AddItemAsync: ShopService not found or not offered. ShopServiceId: {ShopServiceId}, ShopId: {ShopId}, AnonymousUserId: {AnonymousUserId}",
//                 itemDto.ShopServiceId, itemDto.ShopId, anonymousUserId);
//             throw new KeyNotFoundException($"Service with ID {itemDto.ShopServiceId} not found or not currently offered by shop {itemDto.ShopId}.");
//         }

//         // Also fetch Shop Name for the cart item snapshot
//         var shop = await _context.Shops
//             .AsNoTracking()
//             .Select(s => new { s.Id, s.NameEn, s.NameAr }) // Select only needed fields
//             .FirstOrDefaultAsync(s => s.Id == itemDto.ShopId);

//         if (shop == null) {
//             // This case should ideally not happen if shopService was found, as shopService has ShopId
//             _logger.LogError("Shop details not found for ShopId {ShopId} when adding to cart.", itemDto.ShopId);
//             // Handle appropriately - perhaps throw, or proceed without shop name
//         }


//         var existingCartItem = await _context.AnonymousCartItems
//             .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId &&
//                                         ci.ShopId == itemDto.ShopId &&
//                                         ci.ShopServiceId == itemDto.ShopServiceId);

//         if (existingCartItem != null)
//         {
//             existingCartItem.Quantity += itemDto.Quantity;
//             existingCartItem.UpdatedAtUtc = DateTime.UtcNow;
//         }
//         else
//         {
//             var newCartItem = new AnonymousCartItem
//             {
//                 AnonymousUserId = anonymousUserId,
//                 ShopId = itemDto.ShopId,
//                 ShopServiceId = itemDto.ShopServiceId,
//                 Quantity = itemDto.Quantity,
//                 PriceAtAddition = shopService.Price,
//                 ServiceNameSnapshotEn = shopService.EffectiveNameEn,
//                 ServiceNameSnapshotAr = shopService.EffectiveNameAr,
//                 ServiceImageUrlSnapshot = shopService.ShopSpecificIconUrl ?? shopService.GlobalServiceDefinition?.DefaultIconUrl,
//                 AddedAtUtc = DateTime.UtcNow,
//                 UpdatedAtUtc = DateTime.UtcNow,
//                 //  ServiceNameSnapshotEn = shopService.EffectiveNameEn,
//                 // ServiceNameSnapshotAr = shopService.EffectiveNameAr,
//     // --- NEW: Add Shop Name Snapshots ---
//     // ShopNameSnapshotEn = shop?.NameEn ?? "Unknown Shop", // Add to AnonymousCartItem entity
//     // ShopNameSnapshotAr = shop?.NameAr ?? "متجر غير معروف", // Add to AnonymousCartItem entity
//     // // --- END NEW ---
//     // ServiceImageUrlSnapshot = shopService.ShopSpecificIconUrl ?? shopService.GlobalServiceDefinition?.DefaultIconUrl,
    
//             };
//             _context.AnonymousCartItems.Add(newCartItem);
//         }

//         await _context.SaveChangesAsync();
//         _logger.LogInformation("Item ShopServiceId:{ShopServiceId} (Qty: {Quantity}) added/updated in cart for AnonymousUserId: {AnonymousUserId}.",
//             itemDto.ShopServiceId, itemDto.Quantity, anonymousUserId);

//         return await GetCartAsync(anonymousUserId);
//     }

//     // --- NEW METHODS ---

//     public async Task<AnonymousCartApiResponseDto> UpdateItemAsync(string anonymousUserId, Guid anonymousCartItemId, int newQuantity)
//     {
//         var cartItem = await _context.AnonymousCartItems
//             .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId && ci.AnonymousCartItemId == anonymousCartItemId);

//         if (cartItem == null)
//         {
//             _logger.LogWarning("UpdateItemAsync: Cart item not found. AnonymousCartItemId: {AnonymousCartItemId}, AnonymousUserId: {AnonymousUserId}",
//                 anonymousCartItemId, anonymousUserId);
//             throw new KeyNotFoundException($"Cart item with ID {anonymousCartItemId} not found for this anonymous user.");
//         }

//         if (newQuantity <= 0)
//         {
//             // If new quantity is 0 or less, remove the item
//             _context.AnonymousCartItems.Remove(cartItem);
//             _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} removed due to quantity <= 0 for AnonymousUserId: {AnonymousUserId}.",
//                 anonymousCartItemId, anonymousUserId);
//         }
//         else
//         {
//             cartItem.Quantity = newQuantity;
//             cartItem.UpdatedAtUtc = DateTime.UtcNow;
//             _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} quantity updated to {NewQuantity} for AnonymousUserId: {AnonymousUserId}.",
//                 anonymousCartItemId, newQuantity, anonymousUserId);
//         }

//         await _context.SaveChangesAsync();
//         return await GetCartAsync(anonymousUserId);
//     }

//     public async Task<AnonymousCartApiResponseDto> RemoveItemAsync(string anonymousUserId, Guid anonymousCartItemId)
//     {
//         var cartItem = await _context.AnonymousCartItems
//             .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId && ci.AnonymousCartItemId == anonymousCartItemId);

//         if (cartItem == null)
//         {
//             _logger.LogWarning("RemoveItemAsync: Cart item not found for removal. AnonymousCartItemId: {AnonymousCartItemId}, AnonymousUserId: {AnonymousUserId}",
//                 anonymousCartItemId, anonymousUserId);
//             // Optionally, still return current cart or throw KeyNotFoundException
//             // Throwing an exception might be better to indicate the item wasn't there to remove.
//             throw new KeyNotFoundException($"Cart item with ID {anonymousCartItemId} not found for this anonymous user to remove.");
//         }

//         _context.AnonymousCartItems.Remove(cartItem);
//         await _context.SaveChangesAsync();
//         _logger.LogInformation("Item AnonymousCartItemId:{AnonymousCartItemId} removed from cart for AnonymousUserId: {AnonymousUserId}.",
//             anonymousCartItemId, anonymousUserId);

//         return await GetCartAsync(anonymousUserId);
//     }

//     public async Task ClearCartAsync(string anonymousUserId)
//     {
//         var cartItems = await _context.AnonymousCartItems
//             .Where(ci => ci.AnonymousUserId == anonymousUserId)
//             .ToListAsync();

//         if (cartItems.Any())
//         {
//             _context.AnonymousCartItems.RemoveRange(cartItems);
//             await _context.SaveChangesAsync();
//             _logger.LogInformation("Cart cleared for AnonymousUserId: {AnonymousUserId}. Removed {ItemCount} items.",
//                 anonymousUserId, cartItems.Count);
//         }
//         else
//         {
//             _logger.LogInformation("Attempted to clear cart for AnonymousUserId: {AnonymousUserId}, but cart was already empty.", anonymousUserId);
//         }
//         // No need to return the cart here, as the endpoint will likely return 204 No Content.
//     }
// }
// // // src/AutomotiveServices.Api/Services/AnonymousCartService.cs
// // using AutomotiveServices.Api.Data;
// // using AutomotiveServices.Api.Dtos;
// // using AutomotiveServices.Api.Models;
// // using Microsoft.EntityFrameworkCore;
// // using Microsoft.Extensions.Logging;
// // using System;
// // using System.Linq;
// // using System.Threading.Tasks;
// // using System.Collections.Generic;

// // namespace AutomotiveServices.Api.Services;

// // public class AnonymousCartService : IAnonymousCartService
// // {
// //     private readonly AppDbContext _context;
// //     private readonly ILogger<AnonymousCartService> _logger;

// //     public AnonymousCartService(AppDbContext context, ILogger<AnonymousCartService> logger)
// //     {
// //         _context = context;
// //         _logger = logger;
// //     }

// //     private async Task<List<AnonymousCartItemDto>> GetCartItemsDtoAsync(string anonymousUserId)
// //     {
// //         return await _context.AnonymousCartItems
// //             .AsNoTracking()
// //             .Where(ci => ci.AnonymousUserId == anonymousUserId)
// //             .OrderByDescending(ci => ci.AddedAtUtc)
// //             .Select(ci => new AnonymousCartItemDto
// //             {
// //                 AnonymousCartItemId = ci.AnonymousCartItemId,
// //                 ShopId = ci.ShopId,
// //                 ShopServiceId = ci.ShopServiceId,
// //                 Quantity = ci.Quantity,
// //                 ServiceNameEn = ci.ServiceNameSnapshotEn,
// //                 ServiceNameAr = ci.ServiceNameSnapshotAr,
// //                 PriceAtAddition = ci.PriceAtAddition,
// //                 ServiceImageUrlSnapshot = ci.ServiceImageUrlSnapshot,
// //                 AddedAt = ci.AddedAtUtc
// //             })
// //             .ToListAsync();
// //     }

// //     public async Task<AnonymousCartApiResponseDto> GetCartAsync(string anonymousUserId)
// //     {
// //         var cartItemsDto = await GetCartItemsDtoAsync(anonymousUserId);

// //         return new AnonymousCartApiResponseDto
// //         {
// //             AnonymousUserId = anonymousUserId,
// //             Items = cartItemsDto,
// //             TotalItems = cartItemsDto.Sum(i => i.Quantity),
// //             TotalAmount = cartItemsDto.Sum(i => i.PriceAtAddition * i.Quantity),
// //             LastUpdatedAt = cartItemsDto.Any() ? cartItemsDto.Max(i => i.AddedAt) : DateTime.UtcNow
// //         };
// //     }

// //     public async Task<AnonymousCartApiResponseDto> AddItemAsync(string anonymousUserId, AddToAnonymousCartRequestDto itemDto)
// //     {
// //         // 1. Fetch the ShopService to get its current details
// //         var shopService = await _context.ShopServices
// //             .AsNoTracking()
// //             .Include(ss => ss.GlobalServiceDefinition) // For fallback names/images
// //             .FirstOrDefaultAsync(ss => ss.ShopId == itemDto.ShopId && ss.ShopServiceId == itemDto.ShopServiceId && ss.IsOfferedByShop);

// //         if (shopService == null)
// //         {
// //             _logger.LogWarning("Attempted to add non-existent/inactive ShopService (ID: {ShopServiceId}, ShopID: {ShopId}) to anonymous cart for {AnonymousUserId}.",
// //                 itemDto.ShopServiceId, itemDto.ShopId, anonymousUserId);
// //             throw new KeyNotFoundException($"Service with ID {itemDto.ShopServiceId} not found or not currently offered by shop {itemDto.ShopId}.");
// //         }

// //         // 2. Check if item already exists in anonymous cart for this user & service
// //         var existingCartItem = await _context.AnonymousCartItems
// //             .FirstOrDefaultAsync(ci => ci.AnonymousUserId == anonymousUserId &&
// //                                         ci.ShopId == itemDto.ShopId &&
// //                                         ci.ShopServiceId == itemDto.ShopServiceId);

// //         if (existingCartItem != null)
// //         {
// //             existingCartItem.Quantity += itemDto.Quantity;
// //             existingCartItem.UpdatedAtUtc = DateTime.UtcNow;
// //             // PriceAtAddition remains from the first time it was added
// //         }
// //         else
// //         {
// //             var newCartItem = new AnonymousCartItem
// //             {
// //                 AnonymousUserId = anonymousUserId,
// //                 ShopId = itemDto.ShopId,
// //                 ShopServiceId = itemDto.ShopServiceId,
// //                 Quantity = itemDto.Quantity,
// //                 PriceAtAddition = shopService.Price,
// //                 ServiceNameSnapshotEn = shopService.EffectiveNameEn, // Assuming EffectiveName is populated
// //                 ServiceNameSnapshotAr = shopService.EffectiveNameAr, // Assuming EffectiveName is populated
// //                 ServiceImageUrlSnapshot = shopService.ShopSpecificIconUrl ?? shopService.GlobalServiceDefinition?.DefaultIconUrl,
// //                 AddedAtUtc = DateTime.UtcNow,
// //                 UpdatedAtUtc = DateTime.UtcNow
// //             };
// //             _context.AnonymousCartItems.Add(newCartItem);
// //         }

// //         await _context.SaveChangesAsync();
// //         _logger.LogInformation("Item ShopServiceId:{ShopServiceId} (Qty: {Quantity}) added/updated in cart for anonymous user (anon_id_from_token): {AnonymousUserId}.",
// //             itemDto.ShopServiceId, itemDto.Quantity, anonymousUserId);

// //         return await GetCartAsync(anonymousUserId); // Return the updated cart
// //     }
// // }