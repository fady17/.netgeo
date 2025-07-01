// src/AutomotiveServices.Api/Services/IUserCartService.cs
using AutomotiveServices.Api.Dtos; // For DTOs
using System; // For Guid
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public interface IUserCartService
{
    /// <summary>
    /// Retrieves the cart for a given authenticated user.
    /// </summary>
    /// <param name="userId">The unique identifier for the authenticated user.</param>
    /// <returns>The user's cart DTO.</returns>
    Task<UserCartApiResponseDto> GetCartAsync(string userId);

    /// <summary>
    /// Adds an item to the authenticated user's cart.
    /// </summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="itemDto">The item details to add.</param>
    /// <returns>The updated user's cart DTO.</returns>
    Task<UserCartApiResponseDto> AddItemAsync(string userId, AddToUserCartRequestDto itemDto);

    /// <summary>
    /// Updates the quantity of an item in the authenticated user's cart.
    /// If new quantity is 0 or less, the item is removed.
    /// </summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="userCartItemId">The ID of the cart item to update.</param>
    /// <param name="newQuantity">The new quantity for the item.</param>
    /// <returns>The updated user's cart DTO.</returns>
    Task<UserCartApiResponseDto> UpdateItemAsync(string userId, Guid userCartItemId, int newQuantity);

    /// <summary>
    /// Removes an item from the authenticated user's cart.
    /// </summary>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="userCartItemId">The ID of the cart item to remove.</param>
    /// <returns>The updated user's cart DTO.</returns>
    Task<UserCartApiResponseDto> RemoveItemAsync(string userId, Guid userCartItemId);

    /// <summary>
    /// Clears all items from the authenticated user's cart.
    /// </summary>
    /// <param name="userId">The user's identifier.</param>
    Task ClearCartAsync(string userId);
}