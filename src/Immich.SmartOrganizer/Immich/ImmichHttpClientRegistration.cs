using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Immich.SmartOrganizer.Immich;

internal static class ImmichHttpClientRegistration
{
    public const string ClientName = "ImmichApiClient";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private const int MaximumRetries = 2;

    public static IServiceCollection AddImmichHttpClient(
        this IServiceCollection services,
        Func<HttpMessageHandler>? primaryHandlerFactory = null)
    {
        var clientBuilder = services
            .AddHttpClient(ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(
                primaryHandlerFactory ?? (() => new HttpClientHandler { AllowAutoRedirect = false }));

        clientBuilder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = MaximumRetries;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = false;
            options.Retry.MaxDelay = MaximumRetryDelay;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.AttemptTimeout.Timeout = AttemptTimeout;
            options.TotalRequestTimeout.Timeout =
                (AttemptTimeout * (MaximumRetries + 1)) + (MaximumRetryDelay * MaximumRetries);
        });

        clientBuilder.AddHttpMessageHandler(() => new ResponseBufferingHandler());
        return services;
    }

    private sealed class ResponseBufferingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var bufferingSucceeded = false;
            try
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                bufferingSucceeded = true;
                return response;
            }
            finally
            {
                if (!bufferingSucceeded)
                {
                    response.Dispose();
                }
            }
        }
    }
}
