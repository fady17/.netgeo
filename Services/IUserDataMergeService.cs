// src/AutomotiveServices.Api/Services/IUserDataMergeService.cs
using AutomotiveServices.Api.Dtos; // For MergeAnonymousDataResponseDto
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public interface IUserDataMergeService
{
    /// <summary>
    /// Merges data (cart, preferences) from an anonymous session to an authenticated user's account.
    /// </summary>
    /// <param name="authenticatedUserId">The ID of the authenticated user (e.g., 'sub' claim).</param>
    /// <param name="anonymousSessionTokenToMerge">The signed JWT that identified the anonymous session.</param>
    /// <returns>A response indicating the success and details of the merge operation.</returns>
    Task<MergeAnonymousDataResponseDto> MergeDataAsync(string authenticatedUserId, string anonymousSessionTokenToMerge);
}