// src/AutomotiveServices.Api/Services/UserDataMergeService.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
// Ensure you have a using statement for your UserPreference model if it's in a different namespace
// using AutomotiveServices.Api.Models; 

namespace AutomotiveServices.Api.Services;

public class UserDataMergeService : IUserDataMergeService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserDataMergeService> _logger;

    public UserDataMergeService(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<UserDataMergeService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    private string? ValidateAndExtractAnonId(string anonymousSessionToken)
    {
        var jwtSecret = _configuration["AnonymousSession:JwtSecretKey"];
        var issuer = _configuration["AnonymousSession:Issuer"];
        var audience = _configuration["AnonymousSession:Audience"];

        if (string.IsNullOrEmpty(jwtSecret) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
        {
            _logger.LogError("UserDataMergeService: Anonymous token validation configuration is missing.");
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(jwtSecret);

        try
        {
            tokenHandler.ValidateToken(anonymousSessionToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true, ValidIssuer = issuer,
                ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(60)
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var anonIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "anon_id");
            var subTypeClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub_type");

            if (anonIdClaim != null && !string.IsNullOrEmpty(anonIdClaim.Value) && subTypeClaim?.Value == "anonymous_session")
            {
                return anonIdClaim.Value;
            }
            _logger.LogWarning("UserDataMergeService: Validated anonymous token missing 'anon_id' or 'sub_type'.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UserDataMergeService: Anonymous token validation failed during merge.");
            return null;
        }
    }

    public async Task<MergeAnonymousDataResponseDto> MergeDataAsync(string authenticatedUserId, string anonymousSessionTokenToMerge)
    {
        if (string.IsNullOrEmpty(authenticatedUserId)) throw new ArgumentNullException(nameof(authenticatedUserId));
        
        var responseDetails = new MergeAnonymousDataResponseDto.MergeDetails();
        bool anyDataMergedOrAttempted = false;

        if (string.IsNullOrEmpty(anonymousSessionTokenToMerge))
        {
            _logger.LogInformation("MergeDataAsync: No anonymous session token provided for user {AuthenticatedUserId}. Nothing to merge.", authenticatedUserId);
            return new MergeAnonymousDataResponseDto { Success = true, Message = "No anonymous session to merge.", Details = responseDetails };
        }

        var anonId = ValidateAndExtractAnonId(anonymousSessionTokenToMerge);
        if (string.IsNullOrEmpty(anonId))
        {
            _logger.LogWarning("MergeDataAsync: Invalid or expired anonymous session token for user {AuthenticatedUserId}. Merge aborted.", authenticatedUserId);
            return new MergeAnonymousDataResponseDto { Success = false, Message = "Invalid anonymous session token.", Details = responseDetails };
        }

        _logger.LogInformation("Starting data merge for AuthenticatedUserId: {AuthenticatedUserId} from AnonymousId: {AnonymousId}", authenticatedUserId, anonId);

        // Use a transaction to ensure atomicity of merge and delete operations
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Merge Cart Items
            var anonymousCartItems = await _context.AnonymousCartItems
                .Where(ci => ci.AnonymousUserId == anonId)
                .ToListAsync();

            if (anonymousCartItems.Any())
            {
                anyDataMergedOrAttempted = true;
                var userCartItems = await _context.UserCartItems
                    .Where(uci => uci.UserId == authenticatedUserId)
                    .ToListAsync(); // Load existing user cart items into memory for merging

                foreach (var anonItem in anonymousCartItems)
                {
                    var existingUserItem = userCartItems
                        .FirstOrDefault(uci => uci.ShopId == anonItem.ShopId && uci.ShopServiceId == anonItem.ShopServiceId);

                    if (existingUserItem != null)
                    {
                        // Item exists: Add quantities. Keep user's original PriceAtAddition & AddedAtUtc.
                        existingUserItem.Quantity += anonItem.Quantity;
                        existingUserItem.UpdatedAtUtc = DateTime.UtcNow;
                        responseDetails.DuplicatesHandled++;
                        _logger.LogDebug("MergeCart: Updated quantity for existing item (ShopServiceId: {ShopServiceId}) for user {UserId}", anonItem.ShopServiceId, authenticatedUserId);
                    }
                    else
                    {
                        _context.UserCartItems.Add(new UserCartItem
                        {
                            UserId = authenticatedUserId,
                            ShopId = anonItem.ShopId,
                            ShopServiceId = anonItem.ShopServiceId,
                            Quantity = anonItem.Quantity,
                            PriceAtAddition = anonItem.PriceAtAddition,
                            ServiceNameSnapshotEn = anonItem.ServiceNameSnapshotEn,
                            ServiceNameSnapshotAr = anonItem.ServiceNameSnapshotAr,
                            ShopNameSnapshotEn = anonItem.ShopNameSnapshotEn,
                            ShopNameSnapshotAr = anonItem.ShopNameSnapshotAr,
                            ServiceImageUrlSnapshot = anonItem.ServiceImageUrlSnapshot,
                            AddedAtUtc = anonItem.AddedAtUtc, // Preserve original add time from anonymous cart
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                        responseDetails.CartItemsTransferred++;
                    }
                }
                _context.AnonymousCartItems.RemoveRange(anonymousCartItems);
                _logger.LogInformation("MergeCart: Processed {Count} anonymous cart items for user {UserId} from anonId {AnonId}.", anonymousCartItems.Count, authenticatedUserId, anonId);
            }

            // 2. Merge Preferences (Location focused for now)
            var anonymousPreference = await _context.AnonymousUserPreferences
                .FirstOrDefaultAsync(p => p.AnonymousUserId == anonId);

            if (anonymousPreference != null)
            {
                anyDataMergedOrAttempted = true;
                var userPreference = await _context.UserPreferences
                    .FirstOrDefaultAsync(p => p.UserId == authenticatedUserId);

                var now = DateTime.UtcNow;
                if (userPreference == null)
                {
                    userPreference = new UserPreference { UserId = authenticatedUserId, CreatedAtUtc = now };
                    _context.UserPreferences.Add(userPreference);
                }

                // Merge location: anonymous preference overwrites if it's newer OR user has no preference.
                if (anonymousPreference.LastKnownLatitude.HasValue && anonymousPreference.LastKnownLongitude.HasValue)
                {
                    if (!userPreference.LastSetAtUtc.HasValue || anonymousPreference.LastSetAtUtc > userPreference.LastSetAtUtc.Value)
                    {
                        userPreference.LastKnownLatitude = anonymousPreference.LastKnownLatitude;
                        userPreference.LastKnownLongitude = anonymousPreference.LastKnownLongitude;
                        userPreference.LastKnownLocationAccuracy = anonymousPreference.LastKnownLocationAccuracy;
                        userPreference.LocationSource = anonymousPreference.LocationSource;
                        userPreference.LastSetAtUtc = anonymousPreference.LastSetAtUtc; // Use timestamp from anonymous pref
                        responseDetails.PreferencesTransferred = true;
                        _logger.LogDebug("MergePrefs: Location preference updated for user {UserId} from anonId {AnonId}", authenticatedUserId, anonId);
                    }
                }
                // TODO: Implement merging for OtherPreferencesJson if/when it's used.
                // E.g., if it's a dictionary-like structure, merge keys.

                userPreference.UpdatedAtUtc = now;
                _context.AnonymousUserPreferences.Remove(anonymousPreference);
                _logger.LogInformation("MergePrefs: Processed anonymous preferences for user {UserId} from anonId {AnonId}.", authenticatedUserId, anonId);
            }


            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Data merge committed successfully for AuthenticatedUserId: {AuthenticatedUserId} from AnonymousId: {AnonymousId}.", authenticatedUserId, anonId);
            return new MergeAnonymousDataResponseDto
            {
                Success = true,
                Message = anyDataMergedOrAttempted ? "Anonymous data merged successfully." : "No anonymous data found to merge.",
                Details = responseDetails
            };
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "DbUpdateException during data merge for AuthenticatedUserId: {AuthenticatedUserId}, AnonymousId: {AnonymousId}", authenticatedUserId, anonId);
            return new MergeAnonymousDataResponseDto { Success = false, Message = "Error saving merged data.", Details = responseDetails };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Unexpected error during data merge for AuthenticatedUserId: {AuthenticatedUserId}, AnonymousId: {AnonymousId}", authenticatedUserId, anonId);
            return new MergeAnonymousDataResponseDto { Success = false, Message = "An unexpected error occurred during merge.", Details = responseDetails };
        }
    }
}