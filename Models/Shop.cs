// File: Models/Shop.cs (Updated)
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace AutomotiveServices.Api.Models
{
    public class Shop
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string NameAr { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Slug { get; set; }

        [MaxLength(1000)]
        public string? DescriptionEn { get; set; }

        [MaxLength(1000)]
        public string? DescriptionAr { get; set; }

        [Required]
        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        [Required]
        public Point Location { get; set; } = null!;

        [NotMapped]
        public double Latitude => Location.Y;
        [NotMapped]
        public double Longitude => Location.X;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(1000)]
        public string? ServicesOffered { get; set; } // Consider if this is still primary way to list services

        [MaxLength(500)]
        public string? OpeningHours { get; set; }

        [Required]
        public ShopCategory Category { get; set; } = ShopCategory.Unknown;

        // --- MODIFIED RELATIONSHIP ---
        [Required]
        public int OperationalAreaId { get; set; } // Changed from CityId

        // Navigation property to OperationalArea
        [ForeignKey("OperationalAreaId")]
        public OperationalArea OperationalArea { get; set; } = null!; 
        // --- END MODIFIED RELATIONSHIP ---

        [MaxLength(500)]
        public string? LogoUrl { get; set; }

        public bool IsDeleted { get; set; } = false;
        
        public ICollection<ShopService> ShopServices { get; set; } = new List<ShopService>();

        // Optional: Add CreatedAtUtc and UpdatedAtUtc if not already present and desired
        // public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        // public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
// // Models/Shop.cs - Updated with City relationship
// using System.ComponentModel.DataAnnotations;
// using System.ComponentModel.DataAnnotations.Schema;
// using NetTopologySuite.Geometries;

// namespace AutomotiveServices.Api.Models;

// public class Shop
// {
//     [Key]
//     [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
//     public Guid Id { get; set; }

//     [Required]
//     [MaxLength(200)]
//     public string NameEn { get; set; } = string.Empty;

//     [Required]
//     [MaxLength(200)]
//     public string NameAr { get; set; } = string.Empty;

//     [MaxLength(250)]
//     public string? Slug { get; set; } // URL-friendly identifier for the shop

//     [MaxLength(1000)]
//     public string? DescriptionEn { get; set; }

//     [MaxLength(1000)]
//     public string? DescriptionAr { get; set; }

//     [Required]
//     [MaxLength(500)]
//     public string Address { get; set; } = string.Empty;

//     [Required]
//     public Point Location { get; set; } = null!;

//     [NotMapped]
//     public double Latitude => Location.Y;
//     [NotMapped]
//     public double Longitude => Location.X;

//     [MaxLength(20)]
//     public string? PhoneNumber { get; set; }

//     [MaxLength(1000)]
//     public string? ServicesOffered { get; set; }

//     [MaxLength(500)]
//     public string? OpeningHours { get; set; }

//     [Required]
//     public ShopCategory Category { get; set; } = ShopCategory.Unknown;

//     // [Required]
//     // public int CityId { get; set; }


//     // public City City { get; set; } = null!;
//      // --- THIS IS THE KEY RELATIONSHIP TO UPDATE ---
//     [Required] public int CityId { get; set; } // Currently links to City.Id (int)
//     public City City { get; set; } = null!;   // Navigation property to City
//     // --- ---

//     // Optional: for direct display of shop logo on cards/details
//     [MaxLength(500)]
//     public string? LogoUrl { get; set; }

//     public bool IsDeleted { get; set; } = false;
    

//     public ICollection<ShopService> ShopServices { get; set; } = new List<ShopService>();

// }

// // // Models/Shop.cs
// // using System.ComponentModel.DataAnnotations;
// // using System.ComponentModel.DataAnnotations.Schema;
// // using NetTopologySuite.Geometries;

// // namespace AutomotiveServices.Api.Models;

// // // Define your categories as an enum
// // public enum ShopCategory
// // {
// //     Unknown = 0,
// //     GeneralMaintenance = 1,
// //     CarWash = 2,
// //     TireServices = 3,
// //     OilChange = 4,
// //     EVCharging = 5,
// //     BodyRepairAndPaint = 6,
// //     Diagnostics = 7,
// //     Brakes = 8,
// //     ACRepair = 9,
// //     // Add more as needed, up to your ~12-15 main categories
// //     // Consider an "OtherServices" if something doesn't fit neatly
// // }

// // public class Shop
// // {
// //     [Key]
// //     [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
// //     public Guid Id { get; set; }

// //     [Required]
// //     [MaxLength(200)]
// //     public string NameEn { get; set; } = string.Empty;

// //     [Required]
// //     [MaxLength(200)]
// //     public string NameAr { get; set; } = string.Empty;

// //     [MaxLength(1000)]
// //     public string? DescriptionEn { get; set; }

// //     [MaxLength(1000)]
// //     public string? DescriptionAr { get; set; }

// //     [Required]
// //     [MaxLength(500)]
// //     public string Address { get; set; } = string.Empty;

// //     [Required]
// //     public Point Location { get; set; } = null!;

// //     [NotMapped]
// //     public double Latitude => Location.Y;
// //     [NotMapped]
// //     public double Longitude => Location.X;

// //     [MaxLength(20)]
// //     public string? PhoneNumber { get; set; }

// //     [MaxLength(1000)] 
// //     public string? ServicesOffered { get; set; } // Still useful for keyword search within category

// //     [MaxLength(500)]
// //     public string? OpeningHours { get; set; }

// //     [Required] // Make category required for each shop
// //     public ShopCategory Category { get; set; } = ShopCategory.Unknown;

// //     // REMOVED: public ICollection<ShopServiceCategory> ShopServiceCategories { get; set; } = new List<ShopServiceCategory>();

// //     public bool IsDeleted { get; set; } = false;
// // }
// // // // Models/Shop.cs
// // // using System.ComponentModel.DataAnnotations;
// // // using System.ComponentModel.DataAnnotations.Schema;
// // // using NetTopologySuite.Geometries; // For Point

// // // namespace AutomotiveServices.Api.Models;

// // // public class Shop
// // // {
// // //     [Key]
// // //     [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
// // //     public Guid Id { get; set; }

// // //     [Required]
// // //     [MaxLength(200)] // Updated length
// // //     public string NameEn { get; set; } = string.Empty;

// // //     [Required]
// // //     [MaxLength(200)] // Updated length
// // //     public string NameAr { get; set; } = string.Empty;

// // //     [MaxLength(1000)] // Updated length
// // //     public string? DescriptionEn { get; set; }

// // //     [MaxLength(1000)] // Updated length
// // //     public string? DescriptionAr { get; set; }

// // //     [Required]
// // //     [MaxLength(500)] // Updated length
// // //     public string Address { get; set; } = string.Empty;

// // //     [Required]
// // //     // Configuration for column type will be in DbContext
// // //     public Point Location { get; set; } = null!;

// // //     // NotMapped properties for Latitude and Longitude for DTOs/convenience
// // //     // EF Core will try to map these if not [NotMapped] and type is geography
// // //     // To ensure they are derived from Location and not separate columns if Location is geography:
// // //     [NotMapped]
// // //     public double Latitude => Location.Y;

// // //     [NotMapped]
// // //     public double Longitude => Location.X;

// // //     [MaxLength(20)]
// // //     public string? PhoneNumber { get; set; }

// // //     [MaxLength(1000)] // Updated length
// // //     public string? ServicesOffered { get; set; }

// // //     [MaxLength(500)] // Updated length
// // //     public string? OpeningHours { get; set; }

// // //     // CreatedAt and UpdatedAt removed
// // //     // public DateTime CreatedAt { get; set; }
// // //     // public DateTime UpdatedAt { get; set; }

// // //     public bool IsDeleted { get; set; } = false;
// // // }