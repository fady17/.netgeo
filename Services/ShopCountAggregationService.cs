// File: Services/ShopCountAggregationService.cs
using AutomotiveServices.Api.Data;
using AutomotiveServices.Api.Models;
using AutomotiveServices.Api.Dtos; // For AdminBoundaryShopCountDto
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // For IConfiguration
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using Polly;

namespace AutomotiveServices.Api.Services
{
    public class ShopCountAggregationService : IHostedService, IDisposable
    {
        private readonly ILogger<ShopCountAggregationService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private Timer? _timer;
        private CancellationTokenSource? _stoppingCts; // For handling shutdown

        private readonly TimeSpan _aggregationInterval;
        private readonly TimeSpan _initialDelay;
        private readonly int _retryAttempts;
        private readonly TimeSpan _retryDelay;

        // For preventing overlapping executions (timer and manual trigger)
        private static readonly SemaphoreSlim _aggregationSemaphore = new SemaphoreSlim(1, 1);
        private volatile bool _isAggregationRunningByTimer = false; // To prevent timer re-entrancy

        public ShopCountAggregationService(
            ILogger<ShopCountAggregationService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration) // Inject IConfiguration
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;

            // Read configuration
            _aggregationInterval = TimeSpan.FromHours(_configuration.GetValue<int>("AggregationService:TimerIntervalHours", 24));
            _initialDelay = TimeSpan.FromMinutes(_configuration.GetValue<int>("AggregationService:InitialDelayMinutes", 5));
            _retryAttempts = _configuration.GetValue<int>("AggregationService:RetryAttempts", 3);
            _retryDelay = TimeSpan.FromSeconds(_configuration.GetValue<int>("AggregationService:RetryDelaySeconds", 10));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "ShopCountAggregationService is starting. Aggregation Interval: {Interval}, Initial Delay: {Delay}",
                _aggregationInterval, _initialDelay);

            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _timer = new Timer(DoWorkWrapper, null, _initialDelay, _aggregationInterval);

            return Task.CompletedTask;
        }

        private async void DoWorkWrapper(object? state) // Timer callback
        {
            if (_isAggregationRunningByTimer)
            {
                _logger.LogWarning("ShopCountAggregationService timer tick skipped: A previous timer-initiated aggregation is still running.");
                return;
            }

            if (_stoppingCts == null || _stoppingCts.IsCancellationRequested)
            {
                _logger.LogInformation("ShopCountAggregationService timer tick skipped: Service is stopping or has been stopped.");
                return;
            }

            _isAggregationRunningByTimer = true;
            try
            {
                _logger.LogInformation("ShopCountAggregationService (Timer) is starting its periodic work.");
                // Pass the linked token that respects application shutdown
                await TriggerAggregationAsync(_stoppingCts.Token, "Timer");
                _logger.LogInformation("ShopCountAggregationService (Timer) has finished its periodic work.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ShopCountAggregationService (Timer) work was cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while executing ShopCountAggregationService (Timer) periodic work.");
            }
            finally
            {
                _isAggregationRunningByTimer = false;
            }
        }

        // Public method to be called by the API endpoint or other services
        public async Task<bool> TriggerAggregationAsync(CancellationToken cancellationToken, string triggerSource = "Manual")
        {
            _logger.LogInformation("Shop count aggregation triggered by: {TriggerSource}.", triggerSource);

            if (!await _aggregationSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken)) // Try to acquire semaphore
            {
                _logger.LogWarning("Shop count aggregation is already in progress. Trigger by {TriggerSource} declined.", triggerSource);
                return false; // Indicate that aggregation is already running
            }

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Aggregation cancelled before starting work ({TriggerSource}).", triggerSource);
                    return false; // Indicate cancellation
                }
                await AggregateShopCountsInternal(cancellationToken, triggerSource);
                return true; // Indicate success
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Shop count aggregation was cancelled ({TriggerSource}).", triggerSource);
                return false; // Indicate cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during shop count aggregation ({TriggerSource}).", triggerSource);
                return false; // Indicate failure
            }
            finally
            {
                _aggregationSemaphore.Release();
                _logger.LogInformation("Shop count aggregation semaphore released ({TriggerSource}).", triggerSource);
            }
        }

        private async Task AggregateShopCountsInternal(CancellationToken cancellationToken, string triggerSource)
        {
            _logger.LogInformation("({TriggerSource}) Starting shop count aggregation for Level 1 Administrative Boundaries.", triggerSource);

            // Polly retry policy for transient DB errors
            var retryPolicy = Policy
                .Handle<DbUpdateException>() // Add other transient exception types if needed
                .Or<System.Net.Sockets.SocketException>() // Example for network issues
                .WaitAndRetryAsync(_retryAttempts,
                                   retryAttempt => _retryDelay,
                                   (exception, timeSpan, retryCount, context) =>
                                   {
                                       _logger.LogWarning(exception, "({TriggerSource}) Retry {RetryCount} for AggregateShopCountsInternal after {Delay}s due to {ExceptionType}.",
                                           triggerSource, retryCount, timeSpan.TotalSeconds, exception.GetType().Name);
                                   });

            await retryPolicy.ExecuteAsync(async (ct) =>
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Performance Optimization: Single query to get counts
                    var shopCountsByAdminId = await dbContext.AdministrativeBoundaries
                        .Where(ab => ab.AdminLevel == 1 && ab.IsActive && ab.Boundary != null)
                        .Select(ab => new AdminBoundaryShopCountDto
                        {
                            AdministrativeBoundaryId = ab.Id,
                            ShopCount = dbContext.Shops
                                .Count(s => !s.IsDeleted && s.Location != null && s.Location.Intersects(ab.Boundary!)) // Ensure ab.Boundary is not null here
                        })
                        .ToListAsync(ct); // Materialize the counts

                    if (ct.IsCancellationRequested) { _logger.LogInformation("({TriggerSource}) Aggregation cancelled after fetching counts.", triggerSource); return; }

                    if (!shopCountsByAdminId.Any())
                    {
                        _logger.LogInformation("({TriggerSource}) No active Level 1 Administrative Boundaries with boundaries found, or no shops found for them.", triggerSource);
                        return;
                    }

                    _logger.LogInformation("({TriggerSource}) Calculated shop counts for {Count} governorates.", triggerSource, shopCountsByAdminId.Count);
                    var now = DateTime.UtcNow;
                    int updatedCount = 0;
                    int createdCount = 0;

                    // Efficiently get existing stats
                    var existingStats = await dbContext.AdminAreaShopStats
                        .Where(s => shopCountsByAdminId.Select(sc => sc.AdministrativeBoundaryId).Contains(s.AdministrativeBoundaryId))
                        .ToDictionaryAsync(s => s.AdministrativeBoundaryId, s => s, ct);

                    if (ct.IsCancellationRequested) { _logger.LogInformation("({TriggerSource}) Aggregation cancelled before upserting stats.", triggerSource); return; }

                    List<AdminAreaShopStats> statsToUpdate = new();
                    List<AdminAreaShopStats> statsToAdd = new();

                    foreach (var countData in shopCountsByAdminId)
                    {
                        if (existingStats.TryGetValue(countData.AdministrativeBoundaryId, out var existingStat))
                        {
                            if (existingStat.ShopCount != countData.ShopCount) // Only update if changed
                            {
                                existingStat.ShopCount = countData.ShopCount;
                                existingStat.LastUpdatedAtUtc = now;
                                statsToUpdate.Add(existingStat); // EF will track changes
                                updatedCount++;
                            }
                        }
                        else
                        {
                            statsToAdd.Add(new AdminAreaShopStats
                            {
                                AdministrativeBoundaryId = countData.AdministrativeBoundaryId,
                                ShopCount = countData.ShopCount,
                                LastUpdatedAtUtc = now
                            });
                            createdCount++;
                        }
                    }
                    
                    if (statsToAdd.Any()) dbContext.AdminAreaShopStats.AddRange(statsToAdd);
                    // For statsToUpdate, EF Core tracks changes on attached entities.

                    if (statsToAdd.Any() || statsToUpdate.Any()) // Only save if there are changes
                    {
                        await dbContext.SaveChangesAsync(ct);
                        _logger.LogInformation("({TriggerSource}) Shop count aggregation upsert complete. Created: {Created}, Updated: {Updated} stats records.", triggerSource, createdCount, updatedCount);
                    }
                    else
                    {
                        _logger.LogInformation("({TriggerSource}) No changes to shop counts detected. No stats records were updated or created.", triggerSource);
                    }
                }
            }, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ShopCountAggregationService is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            _stoppingCts?.Cancel(); // Signal cancellation to any ongoing work
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _stoppingCts?.Cancel();
            _stoppingCts?.Dispose();
            _timer?.Dispose();
            _aggregationSemaphore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}