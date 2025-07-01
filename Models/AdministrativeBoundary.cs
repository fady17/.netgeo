// File: Models/AdministrativeBoundary.cs
using NetTopologySuite.Geometries; // Required for Point and Geometry (MultiPolygon)
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models
{
    public class AdministrativeBoundary
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string NameAr { get; set; } = string.Empty;

        /// <summary>
        /// Defines the level of this boundary (e.g., 0 for Country, 1 for Governorate/State, 2 for City/Municipality).
        /// </summary>
        [Required]
        public int AdminLevel { get; set; }

        /// <summary>
        /// Foreign key to self, for hierarchical relationships (e.g., a Governorate's ParentId points to a Country's Id).
        /// Nullable for top-level entities like countries.
        /// </summary>
        public int? ParentId { get; set; }
        [ForeignKey("ParentId")]
        public AdministrativeBoundary? Parent { get; set; }
        public ICollection<AdministrativeBoundary> Children { get; set; } = new List<AdministrativeBoundary>();


        /// <summary>
        /// Standard country code (e.g., ISO 3166-1 alpha-2 like "EG", "US").
        /// Indexed for efficient filtering by country.
        /// </summary>
        [MaxLength(10)] // Increased slightly for flexibility e.g. UN M49 codes
        public string? CountryCode { get; set; }


        /// <summary>
        /// Official code for this boundary level (e.g., ISO 3166-2 for states/provinces, FIPS code, etc.).
        /// Can be useful for linking to external datasets.
        /// </summary>
        [MaxLength(50)]
        public string? OfficialCode { get; set; }


        /// <summary>
        /// The detailed, authoritative boundary polygon(s) for this administrative area.
        /// Stored as MultiPolygon to accommodate areas with multiple disjoint parts (e.g., islands).
        /// </summary>
        public Geometry? Boundary { get; set; } // Will be configured as geography(MultiPolygon, 4326)

        /// <summary>
        /// A simplified version of the Boundary, for faster rendering on overview maps.
        /// </summary>
        public Geometry? SimplifiedBoundary { get; set; } // Will be configured as geography(MultiPolygon, 4326)

        /// <summary>
        /// A representative point (centroid or similar) for this administrative area.
        /// Useful for label placement, quick map centering, or as a fallback.
        /// </summary>
        public Point? Centroid { get; set; } // Will be configured as geography(Point, 4326)


        /// <summary>
        /// Flag to indicate if this administrative boundary record is actively used or relevant.
        /// </summary>
        public bool IsActive { get; set; } = true;


        // Optional: Store when the record was created and last updated
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}