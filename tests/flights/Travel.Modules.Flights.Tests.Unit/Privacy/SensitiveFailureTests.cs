using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class SensitiveFailureTests
{
    [Fact]
    public async Task Failed_payment_body_does_not_enter_logs()
    {
        var log = new CaptureLogger<DuffelFlightBookingProvider>();
        using var http = new HttpClient(new PaymentFailureHandler())
        {
            BaseAddress = new Uri("https://example.invalid"),
        };
        var provider = new DuffelFlightBookingProvider(
            new DuffelClient(http, Options.Create(new DuffelOptions { ApiKey = "fictional" })),
            TimeProvider.System,
            log
        );
        var result = await provider.ConfirmOrderAsync(
            "ord_fictional",
            PaymentRef.New(),
            Travel
                .Modules.Flights.Core.ValueObjects.Money.Create(
                    10m,
                    Travel.Modules.Flights.Core.ValueObjects.CurrencyCode.Create("USD").Value
                )
                .Value,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        log.Messages.Any(m =>
                m.Contains("sensitive-payment@example.test", StringComparison.Ordinal)
            )
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Smtp_failure_does_not_log_or_rethrow_recipient_and_subject()
    {
        const string secret = "sensitive-mail@example.test";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RejectSmtp(listener, secret, cts.Token);
        var log = new CaptureLogger<MailKitEmailSender>();
        var sender = new MailKitEmailSender(
            Options.Create(
                new SmtpOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    FromAddress = "sender@example.test",
                    FromName = "Fictional",
                }
            ),
            log
        );
        var failure = await Should.ThrowAsync<Exception>(() =>
            sender.SendAsync(
                secret,
                "sensitive-subject",
                "fictional body",
                "fictional body",
                cts.Token
            )
        );
        await server;
        log.Messages.Any(m =>
                m.Contains(secret, StringComparison.Ordinal)
                || m.Contains("sensitive-subject", StringComparison.Ordinal)
            )
            .ShouldBeFalse();
        failure.ToString().Contains(secret, StringComparison.Ordinal).ShouldBeFalse();
        failure.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Profile_lookup_cancellation_preserves_token_without_leaking_failure_chain(
        bool taskCancelled,
        bool duringBodyRead
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var handler = new CancelledProfileHandler(cts, taskCancelled, duringBodyRead);
        using var http = new HttpClient(handler);
        var log = new CaptureLogger<KeycloakUserDirectory>();
        var directory = new KeycloakUserDirectory(
            new ProfileHttpClientFactory(http),
            Options.Create(
                new KeycloakAdminOptions
                {
                    AdminBaseUrl = "https://keycloak.invalid",
                    ClientId = "fictional",
                    ClientSecret = "fictional",
                }
            ),
            log
        );
        var pending = directory.GetAsync(Guid.NewGuid(), cts.Token);
        // Capture the actual awaited exception; a task-cancellation assertion helper can synthesize another one.
        OperationCanceledException? error = null;
        try
        {
            await pending;
        }
        catch (OperationCanceledException caught)
        {
            error = caught;
        }
        error.ShouldNotBeNull();
        handler.Calls.ShouldBe(1);
        handler.ContentReads.ShouldBe(duringBodyRead ? 1 : 0);
        pending.IsCanceled.ShouldBeTrue();
        error.CancellationToken.ShouldBe(cts.Token);
        error.ToString().ShouldNotContain("cancelled-profile@example.test");
        error.ToString().ShouldNotContain("private-profile-response");
        error.InnerException.ShouldBeNull();
        log.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Profile_lookup_non_cancellation_retains_safe_fallback()
    {
        using var http = new HttpClient(new FailedProfileHandler());
        var log = new CaptureLogger<KeycloakUserDirectory>();
        var directory = new KeycloakUserDirectory(
            new ProfileHttpClientFactory(http),
            Options.Create(
                new KeycloakAdminOptions
                {
                    AdminBaseUrl = "https://keycloak.invalid",
                    ClientId = "fictional",
                    ClientSecret = "fictional",
                }
            ),
            log
        );
        var owner = Guid.NewGuid();
        var profile = await directory.GetAsync(owner, TestContext.Current.CancellationToken);
        profile.ShouldNotBeNull();
        profile.Email.ShouldBe($"{owner:N}@example.test");
        log.Messages.ShouldHaveSingleItem().ShouldNotContain("private-profile@example.test");
    }

    private sealed class FailedProfileHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("private-profile@example.test")
            );
    }

    private sealed class ProfileHttpClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    private sealed class CancelledProfileHandler(
        CancellationTokenSource cts,
        bool taskCancelled,
        bool duringBodyRead
    ) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int ContentReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            // The upstream operation can cancel independently of the caller token (for example, a timeout).
            var inner = new IOException("private-profile-response");
            OperationCanceledException error = taskCancelled
                ? new TaskCanceledException("cancelled-profile@example.test", inner, cts.Token)
                : new OperationCanceledException(
                    "cancelled-profile@example.test",
                    inner,
                    cts.Token
                );
            if (!duringBodyRead)
                return Task.FromException<HttpResponseMessage>(error);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new CancelledProfileContent(error, () => ContentReads++),
                }
            );
        }
    }

    private sealed class CancelledProfileContent(OperationCanceledException error, Action read)
        : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            read();
            return Task.FromException(error);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static async Task RejectSmtp(TcpListener listener, string secret, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };
        await writer.WriteLineAsync("220 fictional SMTP");
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("EHLO", StringComparison.Ordinal))
                await writer.WriteLineAsync("250 fictional SMTP");
            else if (line.StartsWith("RCPT", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("550 " + secret + " rejected");
                return;
            }
            else
                await writer.WriteLineAsync("250 OK");
        }
    }

    private sealed class PaymentFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                request.Method == HttpMethod.Get
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"data\":{\"id\":\"ord_fictional\",\"total_amount\":\"10.00\",\"total_currency\":\"USD\",\"payment_status\":{\"awaiting_payment\":true,\"payment_required_by\":\"2099-01-01T00:00:00Z\"}}}",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                    : new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("sensitive-payment@example.test"),
                    }
            );
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception) + exception);
    }
}
