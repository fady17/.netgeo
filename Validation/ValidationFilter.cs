// src/AutomotiveServices.Api/Validation/ValidationFilter.cs
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Validation;

public class ValidationFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T> _validator;
    private readonly ILogger<ValidationFilter<T>> _logger;

    public ValidationFilter(IValidator<T> validator, ILogger<ValidationFilter<T>> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        _logger.LogTrace("ValidationFilter for type {ValidationType} invoked for endpoint {EndpointPath}.",
            typeof(T).Name, context.HttpContext.Request.Path);

        // Find the argument of type T to validate, regardless of its position.
        // This is more robust when [AsParameters] is used with other route/DI parameters.
        T? argToValidate = null;
        object? originalArg = null; // Keep track of the original argument object

        for (int i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is T castedArg)
            {
                argToValidate = castedArg;
                originalArg = context.Arguments[i]; // Store the original argument
                _logger.LogTrace("Found argument of type {ValidationType} at index {ArgumentIndex}.", typeof(T).Name, i);
                break;
            }
        }

        if (argToValidate == null)
        {
            _logger.LogWarning(
                "Argument of type {ValidationType} not found in endpoint arguments for validation. Endpoint: {EndpointPath}. Arguments provided: {ArgumentCount}",
                typeof(T).Name,
                context.HttpContext.Request.Path,
                context.Arguments.Count);
            
            // List argument types for debugging
            for(int i = 0; i < context.Arguments.Count; i++)
            {
                _logger.LogDebug("Argument at index {Index}: Type = {Type}, Value = {Value}", i, context.Arguments[i]?.GetType().FullName ?? "null", context.Arguments[i]?.ToString());
            }

            // This is a configuration error. The filter is applied, but the argument isn't there.
            // Proceeding might be okay if T can be optional, but for validation, it's usually required.
            // Let's return a server error as it indicates a misconfiguration of the filter.
            // return Results.Problem(
            //     title: "Validation Configuration Error",
            //     detail: $"Validation target of type {typeof(T).Name} not found in the endpoint's arguments.",
            //     statusCode: StatusCodes.Status500InternalServerError);
            // OR, let it pass through if T could sometimes be legitimately null/absent for validation
             return await next(context);
        }

        var validationResult = await _validator.ValidateAsync(argToValidate);
        if (!validationResult.IsValid)
        {
            _logger.LogInformation("Validation failed for type {ValidationType} on endpoint {EndpointPath}. Errors: {ValidationErrors}",
                typeof(T).Name,
                context.HttpContext.Request.Path,
                string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

            return Results.ValidationProblem(
                validationResult.ToDictionary(),
                title: "One or more validation errors occurred.",
                instance: context.HttpContext.Request.Path
            );
        }

        _logger.LogTrace("Validation successful for type {ValidationType} on endpoint {EndpointPath}.",
            typeof(T).Name, context.HttpContext.Request.Path);
        
        // It's important to pass the original context arguments to the next filter or endpoint.
        // If argToValidate was a struct and got boxed, we might need to update it in context.Arguments
        // if (originalArg != null && !ReferenceEquals(originalArg, argToValidate) && originalArg.GetType().IsValueType)
        // {
        //     for (int i = 0; i < context.Arguments.Count; i++)
        //     {
        //         if (ReferenceEquals(context.Arguments[i], originalArg))
        //         {
        //             context.Arguments[i] = argToValidate; // Update if it's a struct that was modified (though validators usually don't modify)
        //             break;
        //         }
        //     }
        // }
        // For class types (like ShopQueryParameters), this modification is not usually needed as argToValidate is a reference.

        return await next(context);
    }
}
// // src/AutomotiveServices.Api/Validation/ValidationFilter.cs
// using FluentValidation;
// using Microsoft.AspNetCore.Http; // For IResult, Results
// using System.Linq; // For Linq extensions
// using System.Threading.Tasks; // For Task

// namespace AutomotiveServices.Api.Validation;

// public class ValidationFilter<T> : IEndpointFilter where T : class
// {
//     private readonly IValidator<T> _validator;
//     private readonly ILogger<ValidationFilter<T>> _logger; // Added logger

//     public ValidationFilter(IValidator<T> validator, ILogger<ValidationFilter<T>> logger) // Inject logger
//     {
//         _validator = validator;
//         _logger = logger;
//     }

//     public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
//     {
//         _logger.LogTrace("ValidationFilter for type {ValidationType} invoked.", typeof(T).Name);

//         T? argToValidate = context.GetArgument<T>(0); // More robust way to get the argument by type if it's the first one, or find by type

//         // If you have multiple parameters of different types that could be T, you might need:
//         // T? argToValidate = context.Arguments.OfType<T>().FirstOrDefault();

//         if (argToValidate == null)
//         {
//             _logger.LogWarning("Argument of type {ValidationType} not found in endpoint arguments for validation.", typeof(T).Name);
//             // This indicates a configuration error - the filter is applied but the argument isn't there.
//             // Depending on policy, you might return an error or just proceed.
//             // For safety, let's proceed but this should be investigated if it occurs.
//             return await next(context); 
//         }

//         var validationResult = await _validator.ValidateAsync(argToValidate);
//         if (!validationResult.IsValid)
//         {
//             _logger.LogInformation("Validation failed for type {ValidationType}. Errors: {ValidationErrors}", 
//                 typeof(T).Name, 
//                 string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

//             var errors = validationResult.Errors
//                 .GroupBy(e => e.PropertyName, StringComparer.OrdinalIgnoreCase) // Make property names consistent casing for client
//                 .ToDictionary(
//                     g => g.Key.Length > 0 ? char.ToLowerInvariant(g.Key[0]) + g.Key.Substring(1) : "", // camelCase property names
//                     g => g.Select(e => e.ErrorMessage).ToArray()
//                 );
            
//             return Results.ValidationProblem(
//                 errors, 
//                 title: "One or more validation errors occurred.", 
//                 instance: context.HttpContext.Request.Path);
//         }
//          _logger.LogTrace("Validation successful for type {ValidationType}.", typeof(T).Name);
//         return await next(context);
//     }
// }