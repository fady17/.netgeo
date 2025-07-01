// src/AutomotiveServices.Api/Endpoints/UserCartEndpoints.cs
using AutomotiveServices.Api.Dtos;
using AutomotiveServices.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; // For [FromBody], ProblemDetails
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic; // For Dictionary used in ValidationProblem
using System.Security.Claims; // For ClaimTypes
using System.Threading.Tasks;

namespace AutomotiveServices.Api.Endpoints;

public static class UserCartEndpoints
{
    public static void MapUserCartEndpoints(this IEndpointRouteBuilder app)
    {
        // All endpoints in this group will require authentication by default.
        // The specific policy can be defined here or on individual endpoints.
        // Using ".RequireAuthorization()" without a policy name will use the default auth scheme (Bearer)
        // and require an authenticated user.
        var cartGroup = app.MapGroup("/api/users/me/cart")
                             .WithTags("User Cart (Authenticated)")
                             .RequireAuthorization(); // Protect all endpoints in this group

        // GET /api/users/me/cart
        cartGroup.MapGet("/", async (
            ClaimsPrincipal user, // Injected by ASP.NET Core if authenticated
            IUserCartService cartService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserCartEndpoints.GetCart");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier); // 'sub' claim

            if (string.IsNullOrEmpty(userId))
            {
                // This should ideally not happen if .RequireAuthorization() is effective
                logger.LogWarning("GetCart: Authenticated user's ID (sub claim) not found.");
                return Results.Unauthorized(); 
            }

            try
            {
                var cart = await cartService.GetCartAsync(userId);
                return Results.Ok(cart);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GetCart: Error fetching cart for UserId: {UserId}", userId);
                return Results.Problem("An error occurred while fetching your cart.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("GetUserCart")
        .WithSummary("Gets the authenticated user's current cart.")
        .Produces<UserCartApiResponseDto>()
        .Produces(StatusCodes.Status401Unauthorized) // If token is invalid/missing
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/users/me/cart/items
        cartGroup.MapPost("/items", async (
            ClaimsPrincipal user,
            [FromBody] AddToUserCartRequestDto itemDto,
            IUserCartService cartService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserCartEndpoints.AddItem");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("AddItemToCart: Authenticated user's ID (sub claim) not found.");
                return Results.Unauthorized();
            }

            try
            {
                var updatedCart = await cartService.AddItemAsync(userId, itemDto);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "AddItemToCart: {ErrorMessage} for UserId: {UserId}", knfex.Message, userId);
                return Results.NotFound(new ProblemDetails { Title = "Resource Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AddItemToCart: Error for UserId: {UserId}", userId);
                return Results.Problem("An error occurred while adding item to your cart.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("AddItemToUserCart")
        .WithSummary("Adds an item to the authenticated user's cart.")
        .Produces<UserCartApiResponseDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // PUT /api/users/me/cart/items/{userCartItemId:guid}
        cartGroup.MapPut("/items/{userCartItemId:guid}", async (
            ClaimsPrincipal user,
            Guid userCartItemId,
            [FromBody] UpdateUserCartItemQuantityRequestDto quantityDto,
            IUserCartService cartService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserCartEndpoints.UpdateItem");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("UpdateUserCartItem: Authenticated user's ID (sub claim) not found.");
                return Results.Unauthorized();
            }
             if (quantityDto.NewQuantity < 0)
            {
                 return Results.ValidationProblem(new Dictionary<string, string[]> {
                    { nameof(quantityDto.NewQuantity), new[] { "Quantity cannot be negative." }}
                 });
            }

            try
            {
                var updatedCart = await cartService.UpdateItemAsync(userId, userCartItemId, quantityDto.NewQuantity);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "UpdateUserCartItem: {ErrorMessage} for UserId: {UserId}", knfex.Message, userId);
                return Results.NotFound(new ProblemDetails { Title = "Cart Item Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UpdateUserCartItem: Error for UserId: {UserId}, ItemId: {UserCartItemId}", userId, userCartItemId);
                return Results.Problem("An error occurred while updating your cart item.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("UpdateUserCartItemQuantity")
        .WithSummary("Updates an item's quantity in the user's cart. Quantity 0 removes the item.")
        .Produces<UserCartApiResponseDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/users/me/cart/items/{userCartItemId:guid}
        cartGroup.MapDelete("/items/{userCartItemId:guid}", async (
            ClaimsPrincipal user,
            Guid userCartItemId,
            IUserCartService cartService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserCartEndpoints.RemoveItem");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            try
            {
                var updatedCart = await cartService.RemoveItemAsync(userId, userCartItemId);
                return Results.Ok(updatedCart);
            }
            catch (KeyNotFoundException knfex)
            {
                logger.LogWarning(knfex, "RemoveUserCartItem: {ErrorMessage} for UserId: {UserId}", knfex.Message, userId);
                return Results.NotFound(new ProblemDetails { Title = "Cart Item Not Found", Detail = knfex.Message, Status = StatusCodes.Status404NotFound });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RemoveUserCartItem: Error for UserId: {UserId}, ItemId: {UserCartItemId}", userId, userCartItemId);
                return Results.Problem("An error occurred while removing your cart item.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("RemoveUserCartItem")
        .WithSummary("Removes a specific item from the authenticated user's cart.")
        .Produces<UserCartApiResponseDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/users/me/cart
        cartGroup.MapDelete("/", async (
            ClaimsPrincipal user,
            IUserCartService cartService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("UserCartEndpoints.ClearCart");
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            try
            {
                await cartService.ClearCartAsync(userId);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ClearUserCart: Error for UserId: {UserId}", userId);
                return Results.Problem("An error occurred while clearing your cart.", statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ClearUserCart")
        .WithSummary("Clears all items from the authenticated user's cart.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .Produces(StatusCodes.Status401Unauthorized);
    }
}