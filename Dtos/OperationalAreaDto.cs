// File: Dtos/OperationalAreaDto.cs
namespace AutomotiveServices.Api.Dtos
{
    public class OperationalAreaDto
    {
        public int Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public double CentroidLatitude { get; set; }
        public double CentroidLongitude { get; set; }
        public double? DefaultSearchRadiusMeters { get; set; }
        public int? DefaultMapZoomLevel { get; set; } // Optional
        public bool IsActive { get; set; }

        // --- NEW PROPERTY ---
        public string? DisplayLevel { get; set; } 
        // --- END NEW PROPERTY ---

        /// <summary>
        /// GeoJSON representation of the operational area's simplified boundary.
        /// This will be an object structure, not a string, in the final JSON.
        /// </summary>
        public object? Geometry { get; set; } 
                                   // Type 'object?' is used here to allow for a flexible GeoJSON structure.
                                   // The actual serialization to a proper GeoJSON object will be handled
                                   // by the API endpoint, possibly using a GeoJSON serializer library
                                   // that works well with System.Text.Json or Newtonsoft.Json.
    }
}