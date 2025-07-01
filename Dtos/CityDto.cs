// src/AutomotiveServices.Api/Dtos/CityDto.cs
namespace AutomotiveServices.Api.Dtos;

public class CityDto
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? StateProvince { get; set; }
    public string Country { get; set; } = string.Empty;
    public double Latitude { get; set; }  // Added
    public double Longitude { get; set; } // Added
    public bool IsActive { get; set; } // Often useful to know on the frontend too

}