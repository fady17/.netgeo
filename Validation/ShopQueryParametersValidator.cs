// src/AutomotiveServices.Api/Validation/ShopQueryParametersValidator.cs
using AutomotiveServices.Api.Dtos;
using FluentValidation;

namespace AutomotiveServices.Api.Validation;

public class ShopQueryParametersValidator : AbstractValidator<ShopQueryParameters>
{
    public ShopQueryParametersValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name search term cannot exceed 100 characters.");

        RuleFor(x => x.Services)
            .MaximumLength(200).WithMessage("Services filter cannot exceed 200 characters.");
            
        RuleFor(x => x.UserLatitude)
            .InclusiveBetween(-90.0, 90.0).When(x => x.UserLatitude.HasValue)
            .WithMessage("Latitude must be between -90 and 90.");
            
        RuleFor(x => x.UserLongitude)
            .InclusiveBetween(-180.0, 180.0).When(x => x.UserLongitude.HasValue)
            .WithMessage("Longitude must be between -180 and 180.");

        When(x => x.UserLatitude.HasValue != x.UserLongitude.HasValue, () => {
            RuleFor(x => x.UserLatitude).NotNull().WithMessage("Both UserLatitude and UserLongitude must be provided for location-based search, or neither.");
            RuleFor(x => x.UserLongitude).NotNull().WithMessage("Both UserLatitude and UserLongitude must be provided for location-based search, or neither.");
        });
        
        RuleFor(x => x.RadiusInMeters)
            .InclusiveBetween(1, ShopQueryParameters.MaxRadiusInMeters)
                .WithMessage($"If provided, Radius must be between 1 and {ShopQueryParameters.MaxRadiusInMeters} meters.")
            .When(x => x.RadiusInMeters.HasValue);
            
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ShopQueryParameters.MaxPageSize)
            .When(x => x.PageSize.HasValue)
            .WithMessage($"Page size must be between 1 and {ShopQueryParameters.MaxPageSize}.");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.")
            .When(x => x.PageNumber.HasValue);

        var allowedSortValues = new[] { "", null, "distance_asc", "name_asc", "name_desc" };
        RuleFor(x => x.SortBy)
            .Must(sortBy => allowedSortValues.Contains(sortBy?.Trim().ToLowerInvariant()))
            .WithMessage("Invalid SortBy value. Allowed: 'distance_asc', 'name_asc', 'name_desc', or empty for default.");
    }
}
// // src/AutomotiveServices.Api/Validation/ShopQueryParametersValidator.cs
// using AutomotiveServices.Api.Dtos;
// using FluentValidation;

// namespace AutomotiveServices.Api.Validation;

// public class ShopQueryParametersValidator : AbstractValidator<ShopQueryParameters>
// {
//     public ShopQueryParametersValidator()
//     {
//         RuleFor(x => x.Name)
//             .MaximumLength(100).WithMessage("Name search term cannot exceed 100 characters.");

//         RuleFor(x => x.Services)
//             .MaximumLength(200).WithMessage("Services filter cannot exceed 200 characters.");
            
//         RuleFor(x => x.UserLatitude)
//             .InclusiveBetween(-90.0, 90.0).When(x => x.UserLatitude.HasValue)
//             .WithMessage("Latitude must be between -90 and 90.");
            
//         RuleFor(x => x.UserLongitude)
//             .InclusiveBetween(-180.0, 180.0).When(x => x.UserLongitude.HasValue)
//             .WithMessage("Longitude must be between -180 and 180.");

//         // --- MODIFIED GEO PARAMETER VALIDATION ---
//         When(x => x.UserLatitude.HasValue, () => {
//             RuleFor(x => x.UserLongitude)
//                 .NotNull().WithMessage("UserLongitude must be provided if UserLatitude is present for a location search.");
//         });

//         When(x => x.UserLongitude.HasValue, () => {
//             RuleFor(x => x.UserLatitude)
//                 .NotNull().WithMessage("UserLatitude must be provided if UserLongitude is present for a location search.");
//         });
        
//         // RadiusInMeters is now OPTIONAL if UserLatitude and UserLongitude are present.
//         // If RadiusInMeters IS provided, then it must be within the valid range.
//         RuleFor(x => x.RadiusInMeters)
//             .InclusiveBetween(1, ShopQueryParameters.MaxRadiusInMeters)
//                 .WithMessage($"If provided, Radius must be between 1 and {ShopQueryParameters.MaxRadiusInMeters} meters.")
//             .When(x => x.RadiusInMeters.HasValue); // This rule only applies if RadiusInMeters has a value.
//         // --- END OF MODIFIED GEO PARAMETER VALIDATION ---
            
//         RuleFor(x => x.PageSize)
//             .LessThanOrEqualTo(ShopQueryParameters.MaxPageSize).WithMessage($"Page size cannot exceed {ShopQueryParameters.MaxPageSize}.")
//             .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1.")
//             .When(x => x.PageSize.HasValue); 

//         RuleFor(x => x.PageNumber)
//             .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.")
//             .When(x => x.PageNumber.HasValue);

//         var allowedSortValues = new[] { "", null, "distance_asc", "name_asc", "name_desc" };
//         RuleFor(x => x.SortBy)
//             .Must(sortBy => allowedSortValues.Contains(sortBy?.Trim().ToLowerInvariant()))
//             .WithMessage("Invalid SortBy value. Allowed values are: 'distance_asc', 'name_asc', 'name_desc'. Leave empty for default sort.");
//     }
// }
    // // src/AutomotiveServices.Api/Validation/ShopQueryParametersValidator.cs
    // using AutomotiveServices.Api.Dtos; // Your ShopQueryParameters DTO
    // using FluentValidation;

    // namespace AutomotiveServices.Api.Validation;

    // public class ShopQueryParametersValidator : AbstractValidator<ShopQueryParameters>
    // {
    //     public ShopQueryParametersValidator()
    //     {
    //         // Rule for Name (example: optional, but if provided, not excessively long)
    //         RuleFor(x => x.Name)
    //             .MaximumLength(100).WithMessage("Name search term cannot exceed 100 characters.");

    //         // Rule for Services (example: optional, but if provided, not excessively long)
    //         RuleFor(x => x.Services)
    //             .MaximumLength(200).WithMessage("Services filter cannot exceed 200 characters.");

    //         // Rules for geospatial parameters:
    //         // If UserLatitude is provided, UserLongitude must also be provided, and vice-versa.
    //         // And if any geo coord is provided, RadiusInMeters should ideally be present for a meaningful geo-filter.
            
    //         When(x => x.UserLatitude.HasValue, () => {
    //             RuleFor(x => x.UserLongitude)
    //                 .NotNull().WithMessage("UserLongitude must be provided if UserLatitude is present.");
    //         });

    //         When(x => x.UserLongitude.HasValue, () => {
    //             RuleFor(x => x.UserLatitude)
    //                 .NotNull().WithMessage("UserLatitude must be provided if UserLongitude is present.");
    //         });
            
    //         // If performing a geo-search (lat, lon, AND radius are present), validate radius
    //         When(x => x.UserLatitude.HasValue && x.UserLongitude.HasValue, () => {
    //             RuleFor(x => x.RadiusInMeters)
    //                 .NotNull().WithMessage("RadiusInMeters is required for a location-based search.")
    //                 .InclusiveBetween(1, 100000).WithMessage("Radius must be between 1 and 100,000 meters."); // 100km max
    //         });
            
    //         // Ensure Latitude and Longitude are within valid ranges (already handled by DataAnnotations, but can be duplicated here for consistency or if DataAnnotations are removed)
    //         RuleFor(x => x.UserLatitude)
    //             .InclusiveBetween(-90.0, 90.0).When(x => x.UserLatitude.HasValue)
    //             .WithMessage("Latitude must be between -90 and 90.");
                
    //         RuleFor(x => x.UserLongitude)
    //             .InclusiveBetween(-180.0, 180.0).When(x => x.UserLongitude.HasValue)
    //             .WithMessage("Longitude must be between -180 and 180.");

    //         // PageNumber and PageSize already have DataAnnotation ranges,
    //         // but FluentValidation can also enforce them. GetEffectivePage... methods handle defaults.
    //         // The MaxPageSize check is handled by GetEffectivePageSize, but we can add an explicit rule too.
    //         RuleFor(x => x.PageSize)
    //             .LessThanOrEqualTo(ShopQueryParameters.MaxPageSize)
    //                 .WithMessage($"Page size cannot exceed {ShopQueryParameters.MaxPageSize}.")
    //             .GreaterThanOrEqualTo(1)
    //                 .WithMessage("Page size must be at least 1.")
    //             .When(x => x.PageSize.HasValue); // Only validate if PageSize is actually provided by the user

    //         RuleFor(x => x.PageNumber)
    //             .GreaterThanOrEqualTo(1)
    //                 .WithMessage("Page number must be at least 1.")
    //             .When(x => x.PageNumber.HasValue);

    //         // Validate SortBy against a list of allowed values
    //         var allowedSortValues = new[] { "", null, "distance_asc", "name_asc", "name_desc" }; // "" or null for default
    //         RuleFor(x => x.SortBy)
    //             .Must(sortBy => allowedSortValues.Contains(sortBy?.Trim().ToLowerInvariant()))
    //             .WithMessage("Invalid SortBy value. Allowed values are: 'distance_asc', 'name_asc', 'name_desc'. Leave empty for default sort.");
    //     }
    // }