using FocusAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Services;

/// <summary>
/// Records what would be pushed instead of sending it.
/// </summary>
/// <remarks>
/// Real Web Push needs VAPID signing and AES128-GCM payload encryption, which is
/// a dependency decision (WebPush libraries, key management, a VAPID key pair in
/// the secret store) rather than something to guess at. Everything upstream —
/// subscription storage, quiet hours, the importance threshold, the delivery
/// job — is complete and exercised through this port, so swapping in a real
/// sender is a single registration change in
/// <see cref="DependencyInjection"/>. Until then this logs, and says so.
/// </remarks>
public sealed class LoggingPushNotifier(ILogger<LoggingPushNotifier> logger) : IPushNotifier
{
    public Task SendAsync(
        Domain.Entities.Users.PushSubscription subscription,
        PushMessage message,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "FocusAI push (not delivered — no Web Push provider configured) to {Endpoint}: {Title} — {Body} [{Url}]",
            subscription.Endpoint,
            message.Title,
            message.Body,
            message.Url);

        return Task.CompletedTask;
    }
}
