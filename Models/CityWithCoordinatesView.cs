// src/AutomotiveServices.Api/Models/CityWithCoordinatesView.cs
using System.ComponentModel.DataAnnotations;

namespace AutomotiveServices.Api.Models;

public class CityWithCoordinatesView
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string NameEn { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string NameAr { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? StateProvince { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Country { get; set; } = string.Empty;
    
    public bool IsActive { get; set; }
    
    public double Latitude { get; set; }
    
    public double Longitude { get; set; }
}