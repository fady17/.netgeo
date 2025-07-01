// Models/City.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries; // Required for Point


namespace AutomotiveServices.Api.Models;

public class City
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string NameAr { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty; // URL-friendly identifier like "cairo", "alexandria"

    [MaxLength(100)]
    public string? StateProvince { get; set; }

    [Required]
    [MaxLength(100)]
    public string Country { get; set; } = "Egypt";

    public bool IsActive { get; set; } = true;
    // Representative point for the city (e.g., city center)
    [Required]
    public Point Location { get; set; } = null!; // Store as Point for potential spatial queries later

    [NotMapped] // Not stored directly in DB, derived from Location
    public double Latitude => Location.Y;
    [NotMapped]
    public double Longitude => Location.X;


    // Navigation property
    // public ICollection<Shop> Shops { get; set; } = new List<Shop>();
}