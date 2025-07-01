// src/AutomotiveServices.Api/Models/AnonymousUserPreferences.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class AnonymousUserPreference
{
    [Key]
    public Guid AnonymousUserPreferenceId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)] // To store the 'anon_id' (UUID string) from the anonymous session token
    public string AnonymousUserId { get; set; } = string.Empty;

    // --- Location Preferences ---
    public double? LastKnownLatitude { get; set; }

    public double? LastKnownLongitude { get; set; }

    public double? LastKnownLocationAccuracy { get; set; } // In meters, from browser GPS

    [MaxLength(50)] // e.g., "gps", "ip_geoloc", "manual_city_selection"
    public string? LocationSource { get; set; }

    public DateTime LastSetAtUtc { get; set; } = DateTime.UtcNow;


    // --- Placeholder for Future Generic Preferences ---
    // Could store a JSON string representing more complex/varied preferences
    // For example: favorite categories, price ranges, notification settings for anonymous users (if they provide an email without full signup)
    [Column(TypeName = "jsonb")] // Or "TEXT" if your DB provider doesn't have good JSONB support or if structure is very dynamic
    public string? OtherPreferencesJson { get; set; }


    // Timestamps for the record itself
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}