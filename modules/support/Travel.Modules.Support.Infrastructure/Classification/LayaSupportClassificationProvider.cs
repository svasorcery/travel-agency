using System.Net.Http.Headers;
using ErrorOr;
using Microsoft.Extensions.Options;
using Travel.Modules.Support.Application;

namespace Travel.Modules.Support.Infrastructure.Classification;

/// <summary>One explicit inference, with authenticated resident identity observations and no replay.</summary>
public sealed class LayaSupportClassificationProvider : ISupportClassificationProvider, IDisposable
{
    private readonly HttpClient client;
    private readonly LayaServingProfile? profile;
    private readonly object identityGate = new();
    private readonly object cancellationGate = new();
    private readonly CancellationTokenSource identityDisabled = new();
    private readonly CancellationToken identityToken;
    private int identityDrift;
    private bool disposed;

    public LayaSupportClassificationProvider(
        HttpClient client,
        IOptions<LayaClassificationOptions> options
    )
    {
        this.client = client;
        identityToken = identityDisabled.Token;
        profile = LayaServingProfile.Create(options.Value);
    }

    public bool IsEnabled => profile is not null && Volatile.Read(ref identityDrift) == 0;

    public async Task<ErrorOr<SupportProviderClassification>> ClassifyAsync(
        SupportClassificationInput input,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!IsEnabled)
            return Manual(SupportClassificationReason.Disabled);
        if (!LayaProtocol.TryBuildRequest(input.SerializedState, profile!, out var body))
            return Manual(SupportClassificationReason.InputTooLarge);

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, identityToken);
        var attempt = new InferenceAttempt();
        try
        {
            using var before = await SendAsync(
                HttpMethod.Get,
                "/health",
                null,
                SupportClassificationReason.IdentityMismatch,
                attempt,
                lifetime.Token
            );
            var beforeCount = LayaProtocol.ReadHealth(before.RootElement, profile!);
            using var response = await SendAsync(
                HttpMethod.Post,
                "/v1/systemone",
                body,
                SupportClassificationReason.InvalidResponse,
                attempt,
                lifetime.Token
            );
            using var after = await SendAsync(
                HttpMethod.Get,
                "/health",
                null,
                SupportClassificationReason.IdentityMismatch,
                attempt,
                lifetime.Token
            );
            var afterCount = LayaProtocol.ReadHealth(after.RootElement, profile!);
            if (afterCount < beforeCount || afterCount > beforeCount && !profile!.AllowCpuFallback)
                throw new LayaProtocolFailure(SupportClassificationReason.IdentityMismatch);
            var decoded = LayaProtocol.ReadClassification(response.RootElement, profile!);
            // Candidate disclosure and profile retirement have one linearization point.
            lock (identityGate)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                ThrowIfIdentityDisabled();
                return new SupportProviderClassification(
                    decoded.Category,
                    SupportClassificationReason.Ready,
                    profile!.Id,
                    decoded.InputTokens
                );
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LayaProtocolFailure failure)
        {
            if (failure.Reason == SupportClassificationReason.IdentityMismatch)
                DisableProfile();
            return Manual(failure.Reason);
        }
        catch (OperationCanceledException)
        {
            return Manual(
                attempt.InferenceStarted ? SupportClassificationReason.Uncertain
                : Volatile.Read(ref identityDrift) != 0
                    ? SupportClassificationReason.IdentityMismatch
                : SupportClassificationReason.Unavailable
            );
        }
        catch (HttpRequestException)
        {
            return Manual(
                attempt.InferenceStarted ? SupportClassificationReason.Uncertain
                : Volatile.Read(ref identityDrift) != 0
                    ? SupportClassificationReason.IdentityMismatch
                : SupportClassificationReason.Unavailable
            );
        }
        catch (IOException)
        {
            return Manual(
                attempt.InferenceStarted ? SupportClassificationReason.Uncertain
                : Volatile.Read(ref identityDrift) != 0
                    ? SupportClassificationReason.IdentityMismatch
                : SupportClassificationReason.Unavailable
            );
        }
    }

    public void Dispose()
    {
        DisableProfile();
        lock (cancellationGate)
        {
            if (disposed)
                return;
            disposed = true;
            identityDisabled.Dispose();
        }
    }

    private void DisableProfile()
    {
        lock (identityGate)
            Interlocked.Exchange(ref identityDrift, 1);
        // Cancellation callbacks never execute while the dispatch/disclosure gate is held.
        lock (cancellationGate)
        {
            if (!disposed)
                identityDisabled.Cancel();
        }
    }

    private void ThrowIfIdentityDisabled()
    {
        if (Volatile.Read(ref identityDrift) != 0)
            throw new OperationCanceledException(identityToken);
    }

    private sealed class InferenceAttempt
    {
        internal bool InferenceStarted { get; set; }
    }

    private SupportProviderClassification Manual(SupportClassificationReason reason) =>
        new(null, reason, profile?.Id);

    private async Task<System.Text.Json.JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        SupportClassificationReason malformedReason,
        InferenceAttempt attempt,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(profile!.Origin, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8",
            };
        }
        Task<HttpResponseMessage> pending;
        // Only dispatch admission is synchronized; HTTP/inference awaits remain concurrent.
        lock (identityGate)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfIdentityDisabled();
            if (method == HttpMethod.Post)
                attempt.InferenceStarted = true;
            pending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        using var response = await pending;
        if (!response.IsSuccessStatusCode)
            throw new LayaProtocolFailure(SupportClassificationReason.Unavailable);
        return await LayaProtocol.ReadDocumentAsync(response.Content, malformedReason, ct);
    }
}
