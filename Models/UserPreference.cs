// src/AutomotiveServices.Api/Models/UserPreference.cs 
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models;

public class UserPreference
{
    [Key]
    public Guid UserPreferenceId { get; set; } = Guid.NewGuid();
    [Required]
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty; // Authenticated User ID

    public double? LastKnownLatitude { get; set; }
    public double? LastKnownLongitude { get; set; }
    public double? LastKnownLocationAccuracy { get; set; }
    [MaxLength(50)]
    public string? LocationSource { get; set; }
    public DateTime? LastSetAtUtc { get; set; } // Nullable initially

    [Column(TypeName = "jsonb")]
    public string? OtherPreferencesJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}