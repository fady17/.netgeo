namespace AutomotiveServices.Api.Models.Seed
{
    public class ShopSeedDto
    {
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ServicesOffered { get; set; }
        public string? OpeningHours { get; set; }
        public string Category { get; set; } = string.Empty; // String representation of the enum
        public string OriginalCityRefSlug { get; set; } = string.Empty;

        public string? TargetOperationalAreaSlug { get; set; } // NEW FIELD
        public string? Slug { get; set; } 
        public string? LogoUrl { get; set; }
    }
}