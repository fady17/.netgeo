// File: Models/Dtos/MapDataDtos.cs
using System;
using System.Text.Json.Serialization; // For JsonPolymorphic, JsonDerivedType, JsonPropertyName
// No need to import AutomotiveServices.Api.Models here unless MapDataRequestParameters or other DTOs here would need it.

namespace AutomotiveServices.Api.Dtos
{
    /// <summary>
    /// Base class for map features. Configured for polymorphic JSON serialization.
    /// The "type" property will be used as the discriminator.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(AdminAggregateMapFeatureDto), typeDiscriminator: "admin_aggregate")]
    [JsonDerivedType(typeof(ShopPointMapFeatureDto), typeDiscriminator: "shop_point")]
    public abstract class MapFeatureDtoBase
    {
        /// <summary>
        /// Indicates the type of map feature. Also serves as the JSON type discriminator.
        /// </summary>
        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// DTO for representing aggregated shop statistics for an administrative area on the map.
    /// </summary>
    public class AdminAggregateMapFeatureDto : MapFeatureDtoBase
    {
        
        // Override the abstract property to provide the specific discriminator value.
        public override string Type => "admin_aggregate";


        /// <summary>
        /// The ID of the AdministrativeBoundary.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Arabic name of the administrative boundary.
        /// </summary>
        [JsonPropertyName("nameAr")]
        public string NameAr { get; set; } = string.Empty;

        /// <summary>
        /// English name of the administrative boundary (optional).
        /// </summary>
        [JsonPropertyName("nameEn")]
        public string? NameEn { get; set; }

        /// <summary>
        /// Latitude of the centroid of the administrative boundary.
        /// </summary>
        [JsonPropertyName("centroidLat")]
        public double CentroidLat { get; set; }

        /// <summary>
        /// Longitude of the centroid of the administrative boundary.
        /// </summary>
        [JsonPropertyName("centroidLon")]
        public double CentroidLon { get; set; }

        /// <summary>
        /// Total number of shops within this administrative boundary.
        /// </summary>
        [JsonPropertyName("shopCount")]
        public int ShopCount { get; set; }
    }

    /// <summary>
    /// DTO for representing an individual shop point on the map.
    /// </summary>
    public class ShopPointMapFeatureDto : MapFeatureDtoBase
    {
        public override string Type => "shop_point";


        /// <summary>
        /// The ID of the Shop.
        /// </summary>
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Arabic name of the shop.
        /// </summary>
        [JsonPropertyName("nameAr")]
        public string NameAr { get; set; } = string.Empty;

        /// <summary>
        /// English name of the shop (optional).
        /// </summary>
        [JsonPropertyName("nameEn")]
        public string? NameEn { get; set; }

        /// <summary>
        /// Latitude of the shop's location.
        /// </summary>
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        /// <summary>
        /// Longitude of the shop's location.
        /// </summary>
        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        /// <summary>
        /// Shop category as a string (optional).
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>
        /// URL for the shop's logo (optional).
        /// </summary>
        [JsonPropertyName("logoUrl")]
        public string? LogoUrl { get; set; }
    }

    /// <summary>
    /// DTO for capturing map data request parameters from the query string.
    /// </summary>
    public class MapDataRequestParameters
    {
        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "minLat")]
        public double MinLat { get; set; }

        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "minLon")]
        public double MinLon { get; set; }

        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "maxLat")]
        public double MaxLat { get; set; }

        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "maxLon")]
        public double MaxLon { get; set; }

        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "zoomLevel")]
        public int ZoomLevel { get; set; }
    }
}