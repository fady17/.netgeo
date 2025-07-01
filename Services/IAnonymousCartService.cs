// src/AutomotiveServices.Api/Services/IAnonymousCartService.cs
using AutomotiveServices.Api.Dtos;
using System; // For Guid
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public interface IAnonymousCartService
{
    Task<AnonymousCartApiResponseDto> GetCartAsync(string anonymousUserId);
    Task<AnonymousCartApiResponseDto> AddItemAsync(string anonymousUserId, AddToAnonymousCartRequestDto itemDto);
    
    // --- NEW METHOD SIGNATURES ---
    Task<AnonymousCartApiResponseDto> UpdateItemAsync(string anonymousUserId, Guid anonymousCartItemId, int newQuantity);
    Task<AnonymousCartApiResponseDto> RemoveItemAsync(string anonymousUserId, Guid anonymousCartItemId);
    Task ClearCartAsync(string anonymousUserId); // Returns void as the cart will be empty
}