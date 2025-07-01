using AutomotiveServices.Api.Models;

public enum HighLevelConcept { Unknown, Maintenance, Marketplace }

public class CategoryDto
{
    public ShopCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int ShopCount { get; set; }
    public HighLevelConcept Concept { get; set; } // NEW PROPERTY
}