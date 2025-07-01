// src/AutomotiveServices.Api/Endpoints/AnonymousEndpoints.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc; // For [FromBody] and ProblemDetails
using AutomotiveServices.Api.Dtos; // For all our DTOs
using AutomotiveServices.Api.Services; // For IAnonymousCartService

namespace AutomotiveServices.Api.Endpoints;

public static class AnonymousEndpoints
{
    // Helper function to validate anonymous token and extract anon_id
    private static string? ValidateAnonymousTokenAndGetId(
        HttpContext httpContext,
        IConfiguration configuration,
        ILogger logger)
    {
        const string ANONYMOUS_SESSION_TOKEN_HEADER = "X-Anonymous-Token"; // Matches client

        if (!httpContext.Request.Headers.TryGetValue(ANONYMOUS_SESSION_TOKEN_HEADER, out var tokenValues))
        {
            logger.LogDebug("'{HeaderName}' header not found for anonymous operation on path {Path}.", 
                ANONYMOUS_SESSION_TOKEN_HEADER, httpContext.Request.Path);
            return null;
        }

        var tokenString = tokenValues.FirstOrDefault();
        if (string.IsNullOrEmpty(tokenString))
        {
            logger.LogWarning("'{HeaderName}' header was present but empty for anonymous operation on path {Path}.", 
                ANONYMOUS_SESSION_TOKEN_HEADER, httpContext.Request.Path);
            return null;
        }

        var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
        var issuer = configuration["AnonymousSession:Issuer"];
        var audience = configuration["AnonymousSession:Audience"];

        if (string.IsNullOrEmpty(jwtSecret) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
        {
            logger.LogError("Anonymous token validation configuration (Secret, Issuer, or Audience) is missing. Cannot validate anonymous token.");
            // This is a server configuration error critical for security.
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(jwtSecret);

        try
        {
            tokenHandler.ValidateToken(tokenString, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(60) // Allow for minor clock differences
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var anonIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "anon_id");
            var subTypeClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub_type");

            if (anonIdClaim != null && !string.IsNullOrEmpty(anonIdClaim.Value) && subTypeClaim?.Value == "anonymous_session")
            {
                logger.LogDebug("Anonymous token validated successfully for anon_id: {AnonId}", anonIdClaim.Value);
                return anonIdClaim.Value;
            }
            logger.LogWarning("Validated anonymous token missing 'anon_id', 'anon_id' was empty, or 'sub_type' was not 'anonymous_session'.");
            return null;
        }
        catch (SecurityTokenExpiredException ex)
        {
            logger.LogInformation(ex, "Anonymous token validation failed: Token expired."); // Info level, as this is expected
            return null;
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            logger.LogWarning(ex, "Anonymous token validation failed: Invalid signature. Potential tampering or misconfiguration.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anonymous token validation failed due to an unexpected error during ValidateToken.");
            return null;
        }
    }

    public static void MapAnonymousEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/anonymous")
                       .WithTags("Anonymous User Data");

        // POST /api/anonymous/sessions
        group.MapPost("/sessions", (
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Sessions");
            try
            {
                var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
                var issuer = configuration["AnonymousSession:Issuer"];
                var audience = configuration["AnonymousSession:Audience"];
                var tokenLifetimeMinutes = configuration.GetValue<int?>("AnonymousSession:TokenLifetimeMinutes");

                if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32)
                {
                    logger.LogError("AnonymousSession:JwtSecretKey is missing or too short in configuration.");
                    return Results.Problem(detail: "Server configuration error.", statusCode: StatusCodes.Status500InternalServerError);
                }
                if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || !tokenLifetimeMinutes.HasValue)
                {
                    logger.LogError("AnonymousSession Issuer, Audience, or TokenLifetimeMinutes is missing.");
                    return Results.Problem(detail: "Server configuration error.", statusCode: StatusCodes.Status500InternalServerError);
                }

                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
                var anonymousId = Guid.NewGuid().ToString();
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new("sub_type", "anonymous_session"),
                    new("anon_id", anonymousId)
                };
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddMinutes(tokenLifetimeMinutes.Value),
                    Issuer = issuer,
                    Audience = audience,
                    SigningCredentials = credentials
                };
                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                logger.LogInformation("Generated new anonymous session token with anon_id: {AnonymousId}", anonymousId);
                return Results.Ok(new { anonymousSessionToken = tokenString });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating anonymous session token.");
                return Results.Problem(detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("CreateAnonymousSession")
        .WithSummary("Creates a new anonymous session and returns its token.")
        .Produces<object>(StatusCodes.Status200OK, contentType: "application/json") // Specify content type
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // --- Anonymous Cart Endpoints ---
        var cartGroup = group.MapGroup("/cart").WithTags("Anonymous Cart");

        // GET /api/anonymous/cart
        cartGroup.MapGet("/", async (
            HttpContext httpContext,
            IAnonymousCartService cartService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.Get");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }
            var cart = await cartService.GetCartAsync(anonId);
            return Results.Ok(cart);
        })
        .WithName("GetAnonymousCart")
        .WithSummary("Gets the current anonymous user's cart.")
        .Produces<AnonymousCartApiResponseDto>()
        .Produces(StatusCodes.Status401Unauthorized);

        // POST /api/anonymous/cart/items
        cartGroup.MapPost("/items", async (
            HttpContext httpContext,
            [FromBody] AddToAnonymousCartRequestDto itemDto,
            IAnonymousCartService cartService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.AddItem");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var updatedCart = await cartService.AddItemAsync(anonId, itemDto);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "AddItemToAnonymousCart: {ErrorMessage} for AnonymousId: {AnonymousId}", knfex.Message, anonId);
                return Results.NotFound(new ProblemDetails { Title = "Resource Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AddItemToAnonymousCart: Error for AnonymousId: {AnonymousId}", anonId);
                return Results.Problem("An error occurred while adding item to cart.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("AddItemToAnonymousCart")
        .WithSummary("Adds an item to the anonymous user's cart.")
        .Produces<AnonymousCartApiResponseDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // PUT /api/anonymous/cart/items/{anonymousCartItemId:guid}
        cartGroup.MapPut("/items/{anonymousCartItemId:guid}", async (
            HttpContext httpContext,
            Guid anonymousCartItemId,
            [FromBody] UpdateCartItemQuantityRequestDto quantityDto,
            IAnonymousCartService cartService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.UpdateItem");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            if (quantityDto.NewQuantity < 0) // Model validation should ideally catch this, but double check
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> {
                    { nameof(quantityDto.NewQuantity), new[] { "Quantity cannot be negative." }}
                 });
            }

            try
            {
                var updatedCart = await cartService.UpdateItemAsync(anonId, anonymousCartItemId, quantityDto.NewQuantity);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "UpdateAnonymousCartItem: {ErrorMessage} for AnonymousId: {AnonymousId}", knfex.Message, anonId);
                return Results.NotFound(new ProblemDetails { Title = "Cart Item Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UpdateAnonymousCartItem: Error for AnonymousId: {AnonymousId}, ItemId: {ItemId}", anonId, anonymousCartItemId);
                return Results.Problem("An error occurred while updating cart item.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UpdateAnonymousCartItemQuantity")
        .WithSummary("Updates the quantity of an item in the anonymous user's cart. Quantity 0 removes item.")
        .Produces<AnonymousCartApiResponseDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/anonymous/cart/items/{anonymousCartItemId:guid}
        cartGroup.MapDelete("/items/{anonymousCartItemId:guid}", async (
            HttpContext httpContext,
            Guid anonymousCartItemId,
            IAnonymousCartService cartService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.RemoveItem");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            try
            {
                // RemoveItemAsync now returns the updated cart
                var updatedCart = await cartService.RemoveItemAsync(anonId, anonymousCartItemId);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "RemoveAnonymousCartItem: {ErrorMessage} for AnonymousId: {AnonymousId}", knfex.Message, anonId);
                return Results.NotFound(new ProblemDetails { Title = "Cart Item Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RemoveAnonymousCartItem: Error for AnonymousId: {AnonymousId}, ItemId: {ItemId}", anonId, anonymousCartItemId);
                return Results.Problem("An error occurred while removing cart item.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("RemoveAnonymousCartItem")
        .WithSummary("Removes a specific item from the anonymous user's cart.")
        .Produces<AnonymousCartApiResponseDto>() // Returns updated cart
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/anonymous/cart
        cartGroup.MapDelete("/", async (
            HttpContext httpContext,
            IAnonymousCartService cartService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.Clear");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            try
            {
                await cartService.ClearCartAsync(anonId);
                return Results.NoContent(); // Standard for successful DELETE with no body to return
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ClearAnonymousCart: Error for AnonymousId: {AnonymousId}", anonId);
                return Results.Problem("An error occurred while clearing the cart.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ClearAnonymousCart")
        .WithSummary("Clears all items from the anonymous user's cart.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // --- NEW: Anonymous Preferences Endpoints ---
        var prefsGroup = group.MapGroup("/preferences").WithTags("Anonymous Preferences");

        // GET /api/anonymous/preferences/location
        prefsGroup.MapGet("/location", async (
            HttpContext httpContext,
            IAnonymousUserPreferenceService preferenceService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Prefs.GetLocation");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            var preferences = await preferenceService.GetLocationPreferenceAsync(anonId);
            if (preferences == null)
            {
                // It's fine if a user has no preferences saved yet, return 200 OK with null or an empty object.
                // For consistency with potential future GET /preferences returning a full object,
                // let's return OK with the null DTO. Client can handle null.
                logger.LogInformation("No location preference found for AnonymousId: {AnonymousId}", anonId);
                return Results.Ok((AnonymousUserPreferenceDto?)null); // Explicitly cast null to the DTO type for Swagger
            }
            return Results.Ok(preferences);
        })
        .WithName("GetAnonymousLocationPreference")
        .WithSummary("Gets the anonymous user's saved location preference.")
        .Produces<AnonymousUserPreferenceDto>(StatusCodes.Status200OK, contentType: "application/json") // Can be null
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/anonymous/preferences/location
        prefsGroup.MapPut("/location", async (
            HttpContext httpContext,
            [FromBody] UpdateAnonymousLocationRequestDto locationDto,
            IAnonymousUserPreferenceService preferenceService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Prefs.UpdateLocation");
            var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
            if (string.IsNullOrEmpty(anonId))
            {
                return Results.Unauthorized();
            }

            // Basic DTO validation (FluentValidation filter would be more robust for complex DTOs)
            if (locationDto.Latitude < -90 || locationDto.Latitude > 90 ||
                locationDto.Longitude < -180 || locationDto.Longitude > 180)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>{
                    {"Coordinates", new[] {"Latitude or Longitude out of valid range."}}
                });
            }

            try
            {
                var updatedPreference = await preferenceService.UpdateLocationPreferenceAsync(anonId, locationDto);
                return Results.Ok(updatedPreference);
            }
            catch (ArgumentNullException anex) // From service if anonId or DTO is null
            {
                logger.LogWarning(anex, "UpdateLocationPreference: Null argument for AnonymousId: {AnonymousId}", anonId);
                return Results.BadRequest(new ProblemDetails { Title = "Bad Request", Detail = anex.Message, Status = StatusCodes.Status400BadRequest });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UpdateLocationPreference: Error for AnonymousId: {AnonymousId}", anonId);
                return Results.Problem("An error occurred while updating location preference.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UpdateAnonymousLocationPreference")
        .WithSummary("Updates or creates the anonymous user's location preference.")
        .Produces<AnonymousUserPreferenceDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);
        


    }
}
// // src/AutomotiveServices.Api/Endpoints/AnonymousEndpoints.cs
// using Microsoft.AspNetCore.Builder;
// using Microsoft.AspNetCore.Http;
// using Microsoft.AspNetCore.Routing;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Logging; // For ILoggerFactory and ILogger
// using Microsoft.IdentityModel.Tokens;
// using System;
// using System.Collections.Generic;
// using System.IdentityModel.Tokens.Jwt;
// using System.Linq;
// using System.Security.Claims;
// using System.Text;
// using System.Threading.Tasks;
// using Microsoft.AspNetCore.Mvc;
// using AutomotiveServices.Api.Dtos;
// using AutomotiveServices.Api.Services;

// namespace AutomotiveServices.Api.Endpoints;

// public static class AnonymousEndpoints
// {
//     // Helper function to validate anonymous token and extract anon_id
//     private static string? ValidateAnonymousTokenAndGetId(
//         HttpContext httpContext,
//         IConfiguration configuration,
//         ILogger logger) // Changed to accept a generic ILogger instance
//     {
//         const string ANONYMOUS_SESSION_TOKEN_HEADER = "X-Anonymous-Token";

//         if (!httpContext.Request.Headers.TryGetValue(ANONYMOUS_SESSION_TOKEN_HEADER, out var tokenValues))
//         {
//             logger.LogWarning("'{HeaderName}' header not found for anonymous operation.", ANONYMOUS_SESSION_TOKEN_HEADER);
//             return null;
//         }

//         var tokenString = tokenValues.FirstOrDefault();
//         if (string.IsNullOrEmpty(tokenString))
//         {
//             logger.LogWarning("'{HeaderName}' header was empty for anonymous operation.", ANONYMOUS_SESSION_TOKEN_HEADER);
//             return null;
//         }

//         var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
//         var issuer = configuration["AnonymousSession:Issuer"];
//         var audience = configuration["AnonymousSession:Audience"];

//         if (string.IsNullOrEmpty(jwtSecret) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
//         {
//             logger.LogError("Anonymous token validation configuration (Secret, Issuer, or Audience) is missing.");
//             return null;
//         }

//         var tokenHandler = new JwtSecurityTokenHandler();
//         var key = Encoding.ASCII.GetBytes(jwtSecret);

//         try
//         {
//             tokenHandler.ValidateToken(tokenString, new TokenValidationParameters
//             {
//                 ValidateIssuerSigningKey = true,
//                 IssuerSigningKey = new SymmetricSecurityKey(key),
//                 ValidateIssuer = true,
//                 ValidIssuer = issuer,
//                 ValidateAudience = true,
//                 ValidAudience = audience,
//                 ValidateLifetime = true,
//                 ClockSkew = TimeSpan.FromSeconds(60)
//             }, out SecurityToken validatedToken);

//             var jwtToken = (JwtSecurityToken)validatedToken;
//             var anonIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "anon_id");
//             var subTypeClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub_type");

//             if (anonIdClaim != null && subTypeClaim?.Value == "anonymous_session")
//             {
//                 return anonIdClaim.Value;
//             }
//             logger.LogWarning("Validated anonymous token missing 'anon_id' or 'sub_type' claim.");
//             return null;
//         }
//         catch (SecurityTokenExpiredException ex)
//         {
//             logger.LogWarning(ex, "Anonymous token validation failed: Token expired.");
//             return null;
//         }
//         catch (SecurityTokenInvalidSignatureException ex)
//         {
//             logger.LogWarning(ex, "Anonymous token validation failed: Invalid signature.");
//             return null;
//         }
//         catch (Exception ex)
//         {
//             logger.LogError(ex, "Anonymous token validation failed due to an unexpected error.");
//             return null;
//         }
//     }

//     public static void MapAnonymousEndpoints(this IEndpointRouteBuilder app)
//     {
//         var group = app.MapGroup("/api/anonymous")
//                        .WithTags("Anonymous User Data");

//         group.MapPost("/sessions", (
//             IConfiguration configuration,
//             ILoggerFactory loggerFactory) => // Keep ILoggerFactory here
//         {
//             var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Sessions"); // Specific category
//             // ... rest of the /sessions endpoint logic (no change needed here) ...
//             try
//             {
//                 var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
//                 var issuer = configuration["AnonymousSession:Issuer"];
//                 var audience = configuration["AnonymousSession:Audience"];
//                 var tokenLifetimeMinutes = configuration.GetValue<int?>("AnonymousSession:TokenLifetimeMinutes");

//                 if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32)
//                 {
//                     logger.LogError("AnonymousSession:JwtSecretKey is missing or too short in configuration.");
//                     return Results.Problem(detail: "Server configuration error for anonymous sessions.", statusCode: StatusCodes.Status500InternalServerError);
//                 }
//                 if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || !tokenLifetimeMinutes.HasValue)
//                 {
//                     logger.LogError("AnonymousSession Issuer, Audience, or TokenLifetimeMinutes is missing in configuration.");
//                      return Results.Problem(detail: "Server configuration error for anonymous sessions.", statusCode: StatusCodes.Status500InternalServerError);
//                 }

//                 var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
//                 var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
//                 var anonymousId = Guid.NewGuid().ToString();
//                 var claims = new List<Claim>
//                 {
//                     new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
//                     new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
//                     new("sub_type", "anonymous_session"),
//                     new("anon_id", anonymousId)
//                 };
//                 var tokenDescriptor = new SecurityTokenDescriptor
//                 {
//                     Subject = new ClaimsIdentity(claims), Expires = DateTime.UtcNow.AddMinutes(tokenLifetimeMinutes.Value),
//                     Issuer = issuer, Audience = audience, SigningCredentials = credentials
//                 };
//                 var tokenHandler = new JwtSecurityTokenHandler();
//                 var token = tokenHandler.CreateToken(tokenDescriptor);
//                 var tokenString = tokenHandler.WriteToken(token);
//                 logger.LogInformation("Generated new anonymous session token with anon_id: {AnonymousId}", anonymousId);
//                 return Results.Ok(new { anonymousSessionToken = tokenString });
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex, "Error generating anonymous session token.");
//                 return Results.Problem(detail: "An unexpected error occurred while creating an anonymous session.", statusCode: StatusCodes.Status500InternalServerError);
//             }
//         })
//         .WithName("CreateAnonymousSession")
//         .WithSummary("Creates a new anonymous session and returns a token.")
//         .Produces<object>(StatusCodes.Status200OK)
//         .ProducesProblem(StatusCodes.Status500InternalServerError);

//         var cartGroup = group.MapGroup("/cart").WithTags("Anonymous Cart");

//         cartGroup.MapGet("/", async (
//             HttpContext httpContext,
//             IAnonymousCartService cartService,
//             IConfiguration configuration,
//             ILoggerFactory loggerFactory) => // Use ILoggerFactory
//         {
//             // Create a logger instance with a specific category name
//             var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.Get"); 
//             var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger); // Pass the logger instance
//             if (string.IsNullOrEmpty(anonId))
//             {
//                 logger.LogWarning("GET /cart: Valid anonymous token not provided or invalid. Returning 401.");
//                 return Results.Unauthorized();
//             }
//             var cart = await cartService.GetCartAsync(anonId);
//             return Results.Ok(cart);
//         })
//         .WithName("GetAnonymousCart")
//         .WithSummary("Gets the current anonymous user's cart.")
//         .Produces<AnonymousCartApiResponseDto>()
//         .Produces(StatusCodes.Status401Unauthorized);

//         cartGroup.MapPost("/items", async (
//             HttpContext httpContext,
//             [FromBody] AddToAnonymousCartRequestDto itemDto,
//             IAnonymousCartService cartService,
//             IConfiguration configuration,
//             ILoggerFactory loggerFactory) => // Use ILoggerFactory
//         {
//             // Create a logger instance with a specific category name
//             var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Cart.PostItem");
//             var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger); // Pass the logger instance
//             if (string.IsNullOrEmpty(anonId))
//             {
//                 logger.LogWarning("POST /cart/items: Valid anonymous token not provided or invalid. Returning 401.");
//                 return Results.Unauthorized();
//             }

//             try
//             {
//                 var updatedCart = await cartService.AddItemAsync(anonId, itemDto);
//                 return Results.Ok(updatedCart);
//             }
//             catch (KeyNotFoundException knfex)
//             {
//                 logger.LogWarning(knfex, "Failed to add item to anonymous cart for {AnonymousId}: {ErrorMessage}", anonId, knfex.Message);
//                 return Results.NotFound(new ProblemDetails { Title = "Service Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex, "Error adding item to anonymous cart for {AnonymousId}", anonId);
//                 return Results.Problem("An error occurred while adding item to cart.", statusCode: StatusCodes.Status500InternalServerError);
//             }
//         })
//         .WithName("AddItemToAnonymousCart")
//         .WithSummary("Adds an item to the anonymous user's cart.")
//         .Produces<AnonymousCartApiResponseDto>()
//         .ProducesProblem(StatusCodes.Status404NotFound)
//         .ProducesProblem(StatusCodes.Status500InternalServerError)
//         .Produces(StatusCodes.Status401Unauthorized);
//     }
// }
// // // src/AutomotiveServices.Api/Endpoints/AnonymousEndpoints.cs
// // using Microsoft.AspNetCore.Builder;
// // using Microsoft.AspNetCore.Http;
// // using Microsoft.AspNetCore.Routing;
// // using Microsoft.Extensions.Configuration; // For IConfiguration
// // using Microsoft.Extensions.Logging;
// // using Microsoft.IdentityModel.Tokens; // For SymmetricSecurityKey, SigningCredentials
// // using System;
// // using System.Collections.Generic;
// // using System.IdentityModel.Tokens.Jwt; // For JwtSecurityTokenHandler, JwtRegisteredClaimNames
// // using System.Security.Claims;
// // using System.Text; // For Encoding
// // using Microsoft.AspNetCore.Mvc;
// // using AutomotiveServices.Api.Dtos; // For ProblemDetails
// // using AutomotiveServices.Api.Services;

// // namespace AutomotiveServices.Api.Endpoints;

// // public static class AnonymousEndpoints
// // {
// //     // Helper function to validate anonymous token and extract anon_id
// //     // This would typically be in a middleware or auth handler, but for simplicity in endpoint:
// //     private static string? ValidateAnonymousTokenAndGetId(HttpContext httpContext, IConfiguration configuration, ILogger logger)
// //     {
// //         const string ANONYMOUS_SESSION_TOKEN_HEADER = "X-Anonymous-Token"; // Matches client

// //         if (!httpContext.Request.Headers.TryGetValue(ANONYMOUS_SESSION_TOKEN_HEADER, out var tokenValues))
// //         {
// //             logger.LogWarning("'{HeaderName}' header not found for anonymous cart operation.", ANONYMOUS_SESSION_TOKEN_HEADER);
// //             return null;
// //         }
        
// //         var tokenString = tokenValues.FirstOrDefault();
// //         if (string.IsNullOrEmpty(tokenString))
// //         {
// //             logger.LogWarning("'{HeaderName}' header was empty for anonymous cart operation.", ANONYMOUS_SESSION_TOKEN_HEADER);
// //             return null;
// //         }

// //         var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
// //         var issuer = configuration["AnonymousSession:Issuer"];
// //         var audience = configuration["AnonymousSession:Audience"];

// //         if (string.IsNullOrEmpty(jwtSecret) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
// //         {
// //             logger.LogError("Anonymous token validation configuration (Secret, Issuer, or Audience) is missing.");
// //             // This is a server configuration error.
// //             // Depending on policy, you might throw or return null leading to a generic error for the client.
// //             return null; 
// //         }

// //         var tokenHandler = new JwtSecurityTokenHandler();
// //         var key = Encoding.ASCII.GetBytes(jwtSecret);

// //         try
// //         {
// //             tokenHandler.ValidateToken(tokenString, new TokenValidationParameters
// //             {
// //                 ValidateIssuerSigningKey = true,
// //                 IssuerSigningKey = new SymmetricSecurityKey(key),
// //                 ValidateIssuer = true,
// //                 ValidIssuer = issuer,
// //                 ValidateAudience = true,
// //                 ValidAudience = audience,
// //                 ValidateLifetime = true, // Checks nbf and exp
// //                 ClockSkew = TimeSpan.FromSeconds(60) // Allow some clock skew
// //             }, out SecurityToken validatedToken);

// //             var jwtToken = (JwtSecurityToken)validatedToken;
// //             var anonIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "anon_id");
// //             var subTypeClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub_type");

// //             if (anonIdClaim != null && subTypeClaim?.Value == "anonymous_session")
// //             {
// //                 return anonIdClaim.Value;
// //             }
// //             logger.LogWarning("Validated anonymous token missing 'anon_id' or 'sub_type' claim.");
// //             return null;
// //         }
// //         catch (SecurityTokenExpiredException ex)
// //         {
// //             logger.LogWarning(ex, "Anonymous token validation failed: Token expired.");
// //             return null; // Or a specific status/error indicating expiry
// //         }
// //         catch (SecurityTokenInvalidSignatureException ex)
// //         {
// //             logger.LogWarning(ex, "Anonymous token validation failed: Invalid signature.");
// //             return null;
// //         }
// //         catch (Exception ex)
// //         {
// //             logger.LogError(ex, "Anonymous token validation failed due to an unexpected error.");
// //             return null;
// //         }
// //     }


// //     public static void MapAnonymousEndpoints(this IEndpointRouteBuilder app)
// //     {
// //         var group = app.MapGroup("/api/anonymous")
// //                        .WithTags("Anonymous User Data");

// //         // POST /api/anonymous/sessions - Creates and returns a new anonymous session token
// //         group.MapPost("/sessions", (
// //             IConfiguration configuration,
// //             ILoggerFactory loggerFactory) =>
// //         {
// //             var logger = loggerFactory.CreateLogger("AnonymousEndpoints.Sessions");
// //             try
// //             {
// //                 var jwtSecret = configuration["AnonymousSession:JwtSecretKey"];
// //                 var issuer = configuration["AnonymousSession:Issuer"];
// //                 var audience = configuration["AnonymousSession:Audience"];
// //                 var tokenLifetimeMinutes = configuration.GetValue<int?>("AnonymousSession:TokenLifetimeMinutes");

// //                 if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32) // Basic check
// //                 {
// //                     logger.LogError("AnonymousSession:JwtSecretKey is missing or too short in configuration.");
// //                     return Results.Problem(
// //                         detail: "Server configuration error for anonymous sessions.",
// //                         statusCode: StatusCodes.Status500InternalServerError
// //                     );
// //                 }
// //                 if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || !tokenLifetimeMinutes.HasValue)
// //                 {
// //                     logger.LogError("AnonymousSession Issuer, Audience, or TokenLifetimeMinutes is missing in configuration.");
// //                     return Results.Problem(
// //                        detail: "Server configuration error for anonymous sessions.",
// //                        statusCode: StatusCodes.Status500InternalServerError
// //                    );
// //                 }

// //                 var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
// //                 var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

// //                 var anonymousId = Guid.NewGuid().ToString(); // This is the persistent anonymous identifier

// //                 var claims = new List<Claim>
// //                 {
// //                     new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID
// //                     new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
// //                     new("sub_type", "anonymous_session"), // Custom claim to identify token type
// //                     new("anon_id", anonymousId)           // The actual anonymous user ID
// //                     // No 'sub' claim, as this isn't a traditional user subject
// //                 };

// //                 var tokenDescriptor = new SecurityTokenDescriptor
// //                 {
// //                     Subject = new ClaimsIdentity(claims),
// //                     Expires = DateTime.UtcNow.AddMinutes(tokenLifetimeMinutes.Value),
// //                     Issuer = issuer,
// //                     Audience = audience,
// //                     SigningCredentials = credentials
// //                 };

// //                 var tokenHandler = new JwtSecurityTokenHandler();
// //                 var token = tokenHandler.CreateToken(tokenDescriptor);
// //                 var tokenString = tokenHandler.WriteToken(token);

// //                 logger.LogInformation("Generated new anonymous session token with anon_id: {AnonymousId}", anonymousId);

// //                 return Results.Ok(new { anonymousSessionToken = tokenString });
// //             }
// //             catch (Exception ex)
// //             {
// //                 logger.LogError(ex, "Error generating anonymous session token.");
// //                 return Results.Problem(
// //                     detail: "An unexpected error occurred while creating an anonymous session.",
// //                     statusCode: StatusCodes.Status500InternalServerError
// //                 );
// //             }
// //         })
// //         .WithName("CreateAnonymousSession")
// //         .WithSummary("Creates a new anonymous session and returns a token.")
// //         .Produces<object>(StatusCodes.Status200OK) // Returns { anonymousSessionToken: "jwt_string" }
// //         .ProducesProblem(StatusCodes.Status500InternalServerError);

// //         // --- Anonymous Cart Endpoints ---
// //         var cartGroup = group.MapGroup("/cart").WithTags("Anonymous Cart");

// //         // GET /api/anonymous/cart
// //         cartGroup.MapGet("/", async (
// //             HttpContext httpContext, 
// //             IAnonymousCartService cartService,
// //             IConfiguration configuration,
// //             ILogger<AnonymousEndpoints> logger) =>
// //         {
// //             var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
// //             if (string.IsNullOrEmpty(anonId))
// //             {
// //                 // If no token or invalid, an empty cart for a "new" anonymous session could be returned,
// //                 // or a 401/403 if a valid token is strictly required to even GET a cart.
// //                 // For GET, often returning an empty cart for a new/unidentified session is fine.
// //                 // However, our client *should* always have fetched a token first.
// //                 // So, if token is missing/invalid here, it might imply client-side issue or tampering.
// //                 logger.LogWarning("GET /cart: Valid anonymous token not provided or invalid. Returning 401.");
// //                 return Results.Unauthorized(); 
// //             }
// //             var cart = await cartService.GetCartAsync(anonId);
// //             return Results.Ok(cart);
// //         })
// //         .WithName("GetAnonymousCart")
// //         .WithSummary("Gets the current anonymous user's cart.")
// //         .Produces<AnonymousCartApiResponseDto>()
// //         .Produces(StatusCodes.Status401Unauthorized);


// //         // POST /api/anonymous/cart/items
// //         cartGroup.MapPost("/items", async (
// //             HttpContext httpContext,
// //             [FromBody] AddToAnonymousCartRequestDto itemDto,
// //             IAnonymousCartService cartService,
// //             IConfiguration configuration,
// //             ILogger<AnonymousEndpoints> logger) =>
// //         {
// //             var anonId = ValidateAnonymousTokenAndGetId(httpContext, configuration, logger);
// //             if (string.IsNullOrEmpty(anonId))
// //             {
// //                 logger.LogWarning("POST /cart/items: Valid anonymous token not provided or invalid. Returning 401.");
// //                 return Results.Unauthorized(); 
// //             }

// //             try
// //             {
// //                 var updatedCart = await cartService.AddItemAsync(anonId, itemDto);
// //                 return Results.Ok(updatedCart);
// //             }
// //             catch (KeyNotFoundException knfex) // From cartService if shop/service not found
// //             {
// //                 logger.LogWarning(knfex, "Failed to add item to anonymous cart for {AnonymousId}: {ErrorMessage}", anonId, knfex.Message);
// //                 return Results.NotFound(new ProblemDetails { Title = "Service Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
// //             }
// //             catch (Exception ex)
// //             {
// //                 logger.LogError(ex, "Error adding item to anonymous cart for {AnonymousId}", anonId);
// //                 return Results.Problem("An error occurred while adding item to cart.", statusCode: StatusCodes.Status500InternalServerError);
// //             }
// //         })
// //         .WithName("AddItemToAnonymousCart")
// //         .WithSummary("Adds an item to the anonymous user's cart.")
// //         .Produces<AnonymousCartApiResponseDto>()
// //         .ProducesProblem(StatusCodes.Status404NotFound) // For invalid shop/service ID
// //         .ProducesProblem(StatusCodes.Status500InternalServerError)
// //         .Produces(StatusCodes.Status401Unauthorized);
// //     }
// // }