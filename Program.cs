// src/AutomotiveServices.Api/Program.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using System;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using AutomotiveServices.Api.Endpoints.Features.Shops;
using AutomotiveServices.Api.Endpoints;
using AutomotiveServices.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization; // Needed for EndpointConventionBuilderExtensions.AllowAnonymous

var builder = WebApplication.CreateBuilder(args);

// 1. CORS Policy
const string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.WithOrigins("http://localhost:3000")
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                      });
});

// 2. Database Context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseNetTopologySuite()
    ));

// Authentication Service Configuration
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = builder.Configuration["Authentication:Schemes:Bearer:Authority"];
        options.Audience = builder.Configuration["Authentication:Schemes:Bearer:Audience"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            NameClaimType = ClaimTypes.NameIdentifier, RoleClaimType = "role",
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>(); // Get a logger
                logger.LogWarning(context.Exception, "JWT Authentication Failed for path: {Path}", context.Request.Path);
                
                // IMPORTANT: Do not modify the response here if the endpoint allows anonymous access.
                // The AuthorizationMiddleware will handle challenges for protected endpoints.
                // If we write a response here, it will cause "response already started" for public endpoints.
                // Simply failing authentication means context.Principal will not be set or will be unauthenticated.
                // The endpoint itself or AuthorizationMiddleware will decide if that's an issue.
                // context.NoResult(); // This is good, it indicates failure but doesn't short-circuit to a default response.
                return Task.CompletedTask; // Just log and let it pass through.
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Token validated for user: {User} for path: {Path}", 
                    context.Principal?.Identity?.Name, context.Request.Path);
                return Task.CompletedTask;
            },
            OnChallenge = context => // This event is triggered when Authorization fails for a protected endpoint
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT Bearer Challenge triggered for path {Path}. Responding with 401.", context.Request.Path);
                
                // Marks the response as handled, preventing others from trying to write to it.
                context.HandleResponse(); 
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json"; // Or text/plain

                // You can write a custom JSON response body here if desired for 401
                // For Minimal APIs, Results.Unauthorized() is often handled by the framework later if no HandleResponse.
                // Since we HandleResponse, we must write something or it will be an empty 401.
                if (builder.Environment.IsDevelopment())
                {
                    return context.Response.WriteAsJsonAsync(new ProblemDetails {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "Unauthorized",
                        Detail = "Authentication token is invalid, expired, or missing for a protected resource." 
                                 + (context.AuthenticateFailure != null ? " Failure: " + context.AuthenticateFailure.Message : "")
                    });
                }
                return context.Response.WriteAsJsonAsync(new ProblemDetails {
                     Status = StatusCodes.Status401Unauthorized,
                     Title = "Unauthorized"
                });
            }
        };
    });

// Authorization Service Configuration
builder.Services.AddAuthorization(options =>
{
    // This can be your default policy if you uncomment it,
    // meaning all endpoints not marked with .AllowAnonymous() or another policy would require auth.
    // options.DefaultPolicy = new AuthorizationPolicyBuilder()
    //    .RequireAuthenticatedUser()
    //    .Build();

    options.AddPolicy("ApiUser", policy => {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "automotiveservices.api.user.interact");
    });
});


// // 3. OpenAPI / Swagger Services for interactive UI
builder.Services.AddEndpointsApiExplorer(); // Crucial for Minimal APIs to be discovered by Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo // Fully qualify if needed: Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Automotive Services API",
        Version = "v1",
        Description = "API for managing automotive shops, services, cities, and categories. All shop-related endpoints are contextualized by city and subcategory slugs in the URL."
    });

    // --- NEW: Add Security Definition for JWT Bearer to Swagger ---
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>() // new string[] {} 
        }
    });
    
    // Add requirement for custom admin key header if desired in Swagger UI
    options.AddSecurityDefinition("AdminKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Admin-Trigger-Key",
        Description = "Admin secret key for triggering restricted tasks."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "AdminKey" }
            },
            Array.Empty<string>()
        }
    });
});

//     // Optional: If you want to include XML comments from your endpoint summaries
    //     // Ensure your project is configured to generate XML documentation file:
    //     // <PropertyGroup>
    //     //   <GenerateDocumentationFile>true</GenerateDocumentationFile>
    //     //   <NoWarn>$(NoWarn);1591</NoWarn> <!-- Optional: Suppress warnings for uncommented public members -->
    //     // </PropertyGroup>
    //     // var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    //     // options.IncludeXmlComments(System.IO.Path.Combine(AppContext.BaseDirectory, xmlFilename));

// // builder.Services.AddOpenApi(); // You can comment this out if AddSwaggerGen is your primary Swagger mechanism
//                               // Or keep it if you have a separate build-time generation process.
//                               // For just Swagger UI, AddSwaggerGen is sufficient.

// Validation
builder.Services.AddValidatorsFromAssemblyContaining<ShopQueryParametersValidator>();
builder.Services.AddTransient(typeof(ValidationFilter<>));

// Application Services
builder.Services.AddScoped<IAnonymousCartService, AnonymousCartService>();
builder.Services.AddScoped<IAnonymousUserPreferenceService, AnonymousUserPreferenceService>();
builder.Services.AddScoped<IUserCartService, UserCartService>();
builder.Services.AddScoped<IUserDataMergeService, UserDataMergeService>();
builder.Services.AddSingleton<ShopCountAggregationService>(); 
// Also register it as an IHostedService for background operation
builder.Services.AddHostedService(provider => provider.GetRequiredService<ShopCountAggregationService>());


// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Data Seeding Logic

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var loggerForSeeding = services.GetRequiredService<ILogger<Program>>(); // services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        var env = services.GetRequiredService<IWebHostEnvironment>(); // Correct way to get IWebHostEnvironment

        // In development, always re-seed for consistency.
        bool forceReseed = env.IsDevelopment();
        // You could also make this configurable, e.g., via appsettings.json or command-line arg
        // forceReseed = builder.Configuration.GetValue<bool>("ForceReseed", env.IsDevelopment());

        loggerForSeeding.LogInformation("Data Seeding: forceReseed is set to {ForceReseed}", forceReseed);
        await DataSeeder.SeedAsync(dbContext, loggerForSeeding, forceReseed);
    }
    catch (Exception ex)
    {
        var exceptionLogger = services.GetRequiredService<ILogger<Program>>();
        exceptionLogger.LogError(ex, "An error occurred during data seeding.");
        // Consider whether to stop the application if seeding fails critically.
        // For now, it will continue and log the error.
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Automotive Services API V1");
        options.RoutePrefix = string.Empty;
    });
}
else
{
    app.UseExceptionHandler(exceptionHandlerApp => // Custom production error handler
    {
        exceptionHandlerApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var error = exceptionHandlerPathFeature?.Error;
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(error, "Unhandled exception: {ErrorMessage}", error?.Message);
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Please try again later." // Don't expose stack trace in prod
            });
        });
    });
    app.UseHsts(); // Use HTTP Strict Transport Security
}


app.UseHttpsRedirection();
app.UseCors(MyAllowSpecificOrigins);

// Correct order of Auth middleware
app.UseAuthentication(); // This populates HttpContext.User based on token
app.UseAuthorization();  // This checks HttpContext.User against endpoint requirements

// Map Endpoints
GeneralEndpoints.MapGeneralEndpoints(app);
var operationalAreasApiGroup = app.MapGroup("/api/operational-areas"); // New top-level group
var categoriesInAreaGroup = operationalAreasApiGroup.MapGroup("/{areaSlug}/categories");
var shopsApiGroup = categoriesInAreaGroup.MapGroup("/{subCategorySlug}/shops"); // Final group for shops

ShopQueryEndpoints.MapShopQueryEndpoints(shopsApiGroup); 
ShopServiceEndpoints.MapShopServiceEndpoints(shopsApiGroup);
// var shopsApiGroup = app.MapGroup("/api/cities/{citySlug}/categories/{subCategorySlug}/shops");
// ShopQueryEndpoints.MapShopQueryEndpoints(shopsApiGroup); 
// ShopServiceEndpoints.MapShopServiceEndpoints(shopsApiGroup); // Add .AllowAnonymous() to public GETs inside this

AnonymousEndpoints.MapAnonymousEndpoints(app); // These should be public or use X-Anonymous-Token
UserAccountEndpoints.MapUserAccountEndpoints(app); // These are .RequireAuthorization()
UserCartEndpoints.MapUserCartEndpoints(app);
AdminTaskEndpoints.MapAdminTaskEndpoints(app); // Map the new admin task endpoints
MapDataEndpoints.MapMapDataEndpoints(app);

app.Run();
