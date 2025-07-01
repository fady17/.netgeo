// src/AutomotiveServices.Api/Dtos/SubCategoryDto.cs
using AutomotiveServices.Api.Models; // For Enums

namespace AutomotiveServices.Api.Dtos;

public class SubCategoryDto
{
    public ShopCategory SubCategoryEnum { get; set; } // The specific enum value
    public string Name { get; set; } = string.Empty; // User-friendly name (e.g., "General Maintenance")
    public string Slug { get; set; } = string.Empty; // URL slug (e.g., "general-maintenance")
    public int ShopCount { get; set; }
    public Models.HighLevelConcept Concept { get; set; } // Parent concept (Maintenance or Marketplace)
}