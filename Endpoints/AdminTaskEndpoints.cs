// File: Endpoints/AdminTaskEndpoints.cs
using AutomotiveServices.Api.Middleware; 
using AutomotiveServices.Api.Services;    
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; 
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Threading; 
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection; // Required for [FromServices]

namespace AutomotiveServices.Api.Endpoints
{
    public static class AdminTaskEndpoints
    {
        public static void MapAdminTaskEndpoints(this IEndpointRouteBuilder app)
        {
            var adminGroup = app.MapGroup("/api/admin/tasks")
                                .WithTags("Admin Tasks")
                                .AddEndpointFilter<AdminKeyAuthFilter>(); 

            adminGroup.MapPost("/aggregate-shop-counts", 
                async ([FromServices] ShopCountAggregationService aggregationService, 
                       [FromServices] ILoggerFactory loggerFactory, // Use ILoggerFactory
                       HttpContext httpContext) =>
            {
                var logger = loggerFactory.CreateLogger("AdminTaskEndpoints.TriggerAggregation"); // Create a specific logger
                logger.LogInformation("Manual trigger received for shop count aggregation from IP: {RemoteIP}", httpContext.Connection.RemoteIpAddress);
                
                // TriggerAggregationAsync returns true if it successfully acquired the semaphore and started/queued the work,
                // or false if it couldn't (e.g., already running).
                bool acceptedToRun = await aggregationService.TriggerAggregationAsync(httpContext.RequestAborted, "ManualAPI");

                if (acceptedToRun)
                {
                    // The task is accepted and will run (or is running).
                    // The client doesn't need to wait for its full completion.
                    return Results.Accepted(value: new { message = "Shop count aggregation process initiated." });
                }
                else
                {
                    // This means it was already running.
                    return Results.Conflict(new ProblemDetails {
                        Title = "Aggregation Already In Progress",
                        Detail = "Shop count aggregation is already running. Please try again later.",
                        Status = StatusCodes.Status409Conflict
                    });
                }
            })
            .WithName("TriggerShopCountAggregation")
            .WithSummary("Manually triggers the shop count aggregation background process.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized) 
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);  
        }
    }
}