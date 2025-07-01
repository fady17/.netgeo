// src/AutomotiveServices.Api/Services/IAnonymousUserPreferenceService.cs
using AutomotiveServices.Api.Dtos; // For DTOs
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public interface IAnonymousUserPreferenceService
{
    /// <summary>
    /// Retrieves the location preference for a given anonymous user.
    /// </summary>
    /// <param name="anonymousUserId">The unique identifier for the anonymous user (from anon_id claim).</param>
    /// <returns>The user's location preference DTO, or null if not found.</returns>
    Task<AnonymousUserPreferenceDto?> GetLocationPreferenceAsync(string anonymousUserId);

    /// <summary>
    /// Updates or creates the location preference for a given anonymous user.
    /// </summary>
    /// <param name="anonymousUserId">The unique identifier for the anonymous user.</param>
    /// <param name="locationData">The location data to update with.</param>
    /// <returns>The updated user's location preference DTO.</returns>
    Task<AnonymousUserPreferenceDto> UpdateLocationPreferenceAsync(string anonymousUserId, UpdateAnonymousLocationRequestDto locationData);

    // Future methods could include:
    // Task ClearLocationPreferenceAsync(string anonymousUserId);
    // Task<SomeOtherPreferenceDto> GetOtherPreferenceAsync(string anonymousUserId, string preferenceKey);
    // Task UpdateOtherPreferenceAsync(string anonymousUserId, string preferenceKey, object preferenceValue);
}