// src/AutomotiveServices.Api/Services/AnonymousUserPreferenceService.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Models; // For AnonymousUserPreference entity
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Services;

public class AnonymousUserPreferenceService : IAnonymousUserPreferenceService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AnonymousUserPreferenceService> _logger;

    public AnonymousUserPreferenceService(AppDbContext context, ILogger<AnonymousUserPreferenceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AnonymousUserPreferenceDto?> GetLocationPreferenceAsync(string anonymousUserId)
    {
        if (string.IsNullOrEmpty(anonymousUserId))
        {
            // This case should ideally be prevented by the endpoint before calling the service
            _logger.LogWarning("GetLocationPreferenceAsync called with null or empty anonymousUserId.");
            return null;
        }

        var preference = await _context.AnonymousUserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AnonymousUserId == anonymousUserId);

        if (preference == null)
        {
            _logger.LogInformation("No location preference found for anonymous user ID: {AnonymousUserId}", anonymousUserId);
            return null;
        }

        // Map entity to DTO
        return new AnonymousUserPreferenceDto
        {
            LastKnownLatitude = preference.LastKnownLatitude,
            LastKnownLongitude = preference.LastKnownLongitude,
            LastKnownLocationAccuracy = preference.LastKnownLocationAccuracy,
            LocationSource = preference.LocationSource,
            LastSetAtUtc = preference.LastSetAtUtc
            // Map OtherPreferencesJson if needed in the DTO later
        };
    }

    public async Task<AnonymousUserPreferenceDto> UpdateLocationPreferenceAsync(
        string anonymousUserId,
        UpdateAnonymousLocationRequestDto locationData)
    {
        if (string.IsNullOrEmpty(anonymousUserId))
        {
            _logger.LogError("UpdateLocationPreferenceAsync called with null or empty anonymousUserId. This should not happen.");
            throw new ArgumentNullException(nameof(anonymousUserId), "Anonymous user ID cannot be null or empty.");
        }
        if (locationData == null)
        {
            _logger.LogError("UpdateLocationPreferenceAsync called with null locationData for AnonymousUserId: {AnonymousUserId}.", anonymousUserId);
            throw new ArgumentNullException(nameof(locationData), "Location data cannot be null.");
        }

        var preference = await _context.AnonymousUserPreferences
            .FirstOrDefaultAsync(p => p.AnonymousUserId == anonymousUserId);

        var now = DateTime.UtcNow;

        if (preference == null)
        {
            _logger.LogInformation("Creating new location preference for anonymous user ID: {AnonymousUserId}", anonymousUserId);
            preference = new AnonymousUserPreference
            {
                AnonymousUserId = anonymousUserId,
                CreatedAtUtc = now
                // AnonymousUserPreferenceId will be generated
            };
            _context.AnonymousUserPreferences.Add(preference);
        }
        else
        {
            _logger.LogInformation("Updating existing location preference for anonymous user ID: {AnonymousUserId}", anonymousUserId);
        }

        preference.LastKnownLatitude = locationData.Latitude;
        preference.LastKnownLongitude = locationData.Longitude;
        preference.LastKnownLocationAccuracy = locationData.Accuracy;
        preference.LocationSource = locationData.Source;
        preference.LastSetAtUtc = now;
        preference.UpdatedAtUtc = now;
        // OtherPreferencesJson would be updated here if part of the request

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully saved location preference for anonymous user ID: {AnonymousUserId}", anonymousUserId);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error saving location preference for anonymous user ID: {AnonymousUserId}", anonymousUserId);
            // Handle specific DB errors if necessary, e.g., constraint violations
            throw; // Re-throw to allow endpoint to handle as 500 or specific error
        }
        
        // Return the DTO representation of the saved/updated preference
        return new AnonymousUserPreferenceDto
        {
            LastKnownLatitude = preference.LastKnownLatitude,
            LastKnownLongitude = preference.LastKnownLongitude,
            LastKnownLocationAccuracy = preference.LastKnownLocationAccuracy,
            LocationSource = preference.LocationSource,
            LastSetAtUtc = preference.LastSetAtUtc
        };
    }
}