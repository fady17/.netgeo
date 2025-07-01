// src/AutomotiveServices.Api/Endpoints/UserAccountEndpoints.cs
using AutomotiveServices.Api.Dtos; // Ensure this brings in MergeAnonymousDataRequestDto
using AutomotiveServices.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic; // For Dictionary
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Endpoints;

public static class UserAccountEndpoints
{
    public static void MapUserAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users/me")
                       .WithTags("User Account (Authenticated)")
                       .RequireAuthorization(); 

        // POST /api/users/me/merge-anonymous-data
        group.MapPost("/merge-anonymous-data", async (
            ClaimsPrincipal user,
            [FromBody] MergeAnonymousDataRequestDto requestDto, // Use the correct DTO
            IUserDataMergeService mergeService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserAccountEndpoints.MergeData");
            
            // The .RequireAuthorization() should ensure user and user.Identity are not null.
            // If user.Identity is null here, it's a misconfiguration of auth middleware.
            var authenticatedUserId = user.FindFirstValue(ClaimTypes.NameIdentifier); 

            if (string.IsNullOrEmpty(authenticatedUserId))
            {
                logger.LogWarning("MergeData: Authenticated user's ID (sub claim) not found even though endpoint is authorized. This is highly unexpected.");
                // This state implies a serious issue with the authentication pipeline setup.
                return Results.Problem(detail: "User identification failed.", statusCode: StatusCodes.Status500InternalServerError);
            }

            // Model binding validation for [Required] on DTO property should handle empty token string.
            // If requestDto itself is null (e.g. malformed JSON), ASP.NET Core returns 400/415.
            // The [Required] on MergeAnonymousDataRequestDto.AnonymousSessionToken ensures it's provided.
            // The service layer will validate the token itself.
            if (requestDto == null || string.IsNullOrEmpty(requestDto.AnonymousSessionToken))
            {
                 logger.LogInformation("MergeData: Request for UserID {UserId} received with no AnonymousSessionToken in the body. Nothing to merge from token.", authenticatedUserId);
                 // Return a DTO indicating no merge was performed or needed.
                 return Results.Ok(new MergeAnonymousDataResponseDto { Success = true, Message = "No anonymous session token provided to merge." });
            }

            try
            {
                var mergeResult = await mergeService.MergeDataAsync(authenticatedUserId, requestDto.AnonymousSessionToken);
                
                if (mergeResult.Success)
                {
                    logger.LogInformation("MergeData: Successful for UserID {UserId}. Message: {Message}", authenticatedUserId, mergeResult.Message);
                    return Results.Ok(mergeResult);
                }
                else
                {
                    logger.LogWarning("MergeData: Failed for UserID {UserId}. Message: {Message}", authenticatedUserId, mergeResult.Message);
                    if (mergeResult.Message.Contains("Invalid anonymous session"))
                    {
                        return Results.BadRequest(new ProblemDetails { 
                            Title = "Merge Failed", Detail = mergeResult.Message, Status = StatusCodes.Status400BadRequest 
                        });
                    }
                    // For other service-layer handled failures that set Success=false
                    return Results.Ok(mergeResult); // Or a more specific error like 500 if appropriate
                }
            }
            catch (ArgumentNullException anex)
            {
                 logger.LogWarning(anex, "MergeData: Argument null exception for UserID {UserId}. Message: {ErrorMessage}", authenticatedUserId, anex.Message);
                 return Results.BadRequest(new ProblemDetails{ Title="Bad Request", Detail = anex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MergeData: Unexpected error for UserID {UserId}", authenticatedUserId);
                return Results.Problem(
                    detail: "An unexpected error occurred during the data merge process.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Merge Error"
                );
            }
        })
        .WithName("MergeAnonymousUserData")
        .WithSummary("Merges an anonymous user's cart and preferences to the currently authenticated user.")
        .Produces<MergeAnonymousDataResponseDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);


        // GET /api/users/me/profile (Test Protected Endpoint)
        group.MapGet("/profile", (ClaimsPrincipal user) => {
            // Corrected check: .RequireAuthorization() ensures user.Identity is not null and IsAuthenticated is true.
            // So, a direct check for IsAuthenticated is sufficient here after the attribute.
            if (!(user.Identity?.IsAuthenticated ?? false)) { // Defensive check, but attribute should handle.
                 return Results.Unauthorized();
            }

            var claims = user.Claims.Select(c => new { c.Type, c.Value });
            return Results.Ok(new { 
                Message = $"Hello authenticated user '{user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity.Name}'!", // Use GivenName if available
                UserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                Email = user.FindFirstValue(ClaimTypes.Email),
                Claims = claims 
            });
        })
        .WithName("GetUserProfile")
        .WithTags("User Account (Protected)")
        .Produces<object>() 
        .Produces(StatusCodes.Status401Unauthorized);
    }
}