// File: Models/OperationalArea.cs
using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models
{
    // Enum to define how the geometry for this operational area is sourced
    public enum GeometrySourceType
    {
        Undefined = 0,
        Custom = 1,        // Geometry is defined in CustomBoundary/CustomSimplifiedBoundary
        DerivedFromAdmin = 2 // Geometry should be taken from a linked AdministrativeBoundary
    }

    public class OperationalArea
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

        [Required]
        [MaxLength(150)]
        public string Slug { get; set; } = string.Empty; // Unique URL-friendly identifier

        /// <summary>
        /// Indicates if this operational area is currently active for services.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Business-defined level for display or grouping (e.g., Primary City, Major District, Service Zone).
        /// Could be an enum mapped to int in DB.
        /// </summary>
        [MaxLength(50)]
        public string? DisplayLevel { get; set; } // Example: "City", "District", "Region"

        /// <summary>
        /// Representative latitude for this operational area (e.g., center of its custom boundary or linked admin boundary).
        /// </summary>
        [Required]
        public double CentroidLatitude { get; set; }

        /// <summary>
        /// Representative longitude for this operational area.
        /// </summary>
        [Required]
        public double CentroidLongitude { get; set; }

        /// <summary>
        /// A sensible default search radius (in meters) to use when this area is selected by a user.
        /// Can be overridden by user preferences or specific search parameters.
        /// </summary>
        public double? DefaultSearchRadiusMeters { get; set; }

        /// <summary>
        /// Optional: Default map zoom level when this area is selected.
        /// </summary>
        public int? DefaultMapZoomLevel { get; set; }

        /// <summary>
        /// Specifies how the boundary geometry for this operational area is determined.
        /// </summary>
        [Required]
        public GeometrySourceType GeometrySource { get; set; } = GeometrySourceType.Undefined;

        /// <summary>
        /// Custom-defined detailed boundary for this operational area, if not directly derived from an AdministrativeBoundary.
        /// </summary>
        public Geometry? CustomBoundary { get; set; } // geography(MultiPolygon, 4326)

        /// <summary>
        /// Custom-defined simplified boundary for this operational area.
        /// </summary>
        public Geometry? CustomSimplifiedBoundary { get; set; } // geography(MultiPolygon, 4326)

        /// <summary>
        /// Optional Foreign Key to an AdministrativeBoundary.
        /// Used if this OperationalArea directly corresponds to an official admin boundary (GeometrySource = DerivedFromAdmin),
        /// OR if it's a custom area that sits hierarchically within an official one (e.g., "6th Oct" within "Giza Gov.").
        /// </summary>
        public int? PrimaryAdministrativeBoundaryId { get; set; }
        [ForeignKey("PrimaryAdministrativeBoundaryId")]
        public AdministrativeBoundary? PrimaryAdministrativeBoundary { get; set; }


        // Navigation property for Shops within this OperationalArea
        public ICollection<Shop> Shops { get; set; } = new List<Shop>();


        // Standard audit fields
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}