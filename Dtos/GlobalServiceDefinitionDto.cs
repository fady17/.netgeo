// src/AutomotiveServices.Api/Dtos/GlobalServiceDefinitionDto.cs
using AutomotiveServices.Api.Models; // For ShopCategory enum

namespace AutomotiveServices.Api.Dtos;

public class GlobalServiceDefinitionDto
{
    public int GlobalServiceId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string DefaultNameEn { get; set; } = string.Empty;
    public string DefaultNameAr { get; set; } = string.Empty;
    public string? DefaultDescriptionEn { get; set; }
    public string? DefaultDescriptionAr { get; set; }
    public string? DefaultIconUrl { get; set; }
    public int? DefaultEstimatedDurationMinutes { get; set; }
    public bool IsGloballyActive { get; set; }
    public ShopCategory Category { get; set; } // The ShopCategory enum
    public string CategoryName => Category.ToString(); // String representation of the enum
}