// File: Middleware/AdminKeyAuthFilter.cs
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq; // For FirstOrDefault
using System.Threading.Tasks; // For ValueTask

namespace AutomotiveServices.Api.Middleware
{
    public class AdminKeyAuthFilter : IEndpointFilter
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminKeyAuthFilter> _logger;
        private const string AdminKeyHeaderName = "X-Admin-Trigger-Key";

        public AdminKeyAuthFilter(IConfiguration configuration, ILogger<AdminKeyAuthFilter> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var configuredKey = _configuration["AggregationService:AdminTriggerKey"];
            if (string.IsNullOrEmpty(configuredKey))
            {
                _logger.LogError("Admin trigger key is not configured. Denying access.");
                return Results.Problem("Service misconfiguration.", statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(AdminKeyHeaderName, out var providedKeyValues))
            {
                _logger.LogWarning("Admin trigger key header '{HeaderName}' not found.", AdminKeyHeaderName);
                return Results.Problem("Admin key required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var providedKey = providedKeyValues.FirstOrDefault();
            if (providedKey != configuredKey)
            {
                _logger.LogWarning("Invalid admin trigger key provided.");
                return Results.Problem("Invalid admin key.", statusCode: StatusCodes.Status403Forbidden);
            }

            // Optional IP Whitelisting (Example)
            var allowedIPsConfig = _configuration["AggregationService:AllowedAdminTriggerIPs"];
            if (!string.IsNullOrEmpty(allowedIPsConfig))
            {
                var allowedIPs = allowedIPsConfig.Split(',').Select(ip => ip.Trim()).ToList();
                var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
                
                if (remoteIp == null || !allowedIPs.Contains(remoteIp.ToString()))
                {
                     // Handle IPv4 mapped to IPv6 if necessary
                    if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
                    {
                        if (!allowedIPs.Contains(remoteIp.MapToIPv4().ToString()))
                        {
                             _logger.LogWarning("Admin trigger IP '{RemoteIP}' not whitelisted.", remoteIp);
                             return Results.Problem("IP address not allowed.", statusCode: StatusCodes.Status403Forbidden);
                        }
                    }
                    else if (remoteIp != null) // If not mapped and not null, direct check failed
                    {
                        _logger.LogWarning("Admin trigger IP '{RemoteIP}' not whitelisted.", remoteIp);
                        return Results.Problem("IP address not allowed.", statusCode: StatusCodes.Status403Forbidden);
                    }
                    else // remoteIp is null
                    {
                         _logger.LogWarning("Could not determine remote IP for admin trigger.");
                         return Results.Problem("Could not determine remote IP.", statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            _logger.LogInformation("Admin trigger key validated successfully for IP: {RemoteIP}", context.HttpContext.Connection.RemoteIpAddress);
            return await next(context);
        }
    }
}