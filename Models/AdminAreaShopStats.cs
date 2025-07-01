// File: Models/AdminAreaShopStats.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutomotiveServices.Api.Models
{
    public class AdminAreaShopStats
    {
        /// <summary>
        /// Primary Key, and also the Foreign Key to AdministrativeBoundary.Id.
        /// This creates a one-to-one relationship where AdminAreaShopStats is dependent on AdministrativeBoundary.
        /// </summary>
        [Key]
        [ForeignKey(nameof(AdministrativeBoundary))]
        public int AdministrativeBoundaryId { get; set; }

        /// <summary>
        /// The administrative boundary this statistic pertains to.
        /// </summary>
        public AdministrativeBoundary AdministrativeBoundary { get; set; } = null!;

        /// <summary>
        /// Total number of active (non-deleted) shops within this administrative boundary.
        /// </summary>
        public int ShopCount { get; set; }

        /// <summary>
        /// The most recent time these statistics were calculated and updated.
        /// </summary>
        public DateTime LastUpdatedAtUtc { get; set; }

        // Optional: Add more aggregated stats later if needed, e.g.,
        // public int TireServiceShopCount { get; set; }
        // public int OilChangeShopCount { get; set; }
        // public string? PopularCategoriesJson { get; set; } // JSON array of top 3 category slugs
    }
}