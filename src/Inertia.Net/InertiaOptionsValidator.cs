using Microsoft.Extensions.Options;

namespace Inertia.Net;

/// <summary>Rejects invalid <see cref="InertiaOptions"/> at startup (<c>ValidateOnStart</c>), naming every invalid setting.</summary>
internal sealed class InertiaOptionsValidator : IValidateOptions<InertiaOptions>
{
    public ValidateOptionsResult Validate(string? name, InertiaOptions options)
    {
        List<string>? failures = null;
        var ssr = options.Ssr;
        Check(IsDeadline(ssr.Timeout, allowZero: false), "Ssr.Timeout must be positive and supported by CancellationTokenSource, or Timeout.InfiniteTimeSpan.");
        Check(ssr.MaxResponseBytes > 0, "Ssr.MaxResponseBytes must be positive.");
        Check(ssr.MaxPageBytes > 0, "Ssr.MaxPageBytes must be positive.");
        foreach (var url in ssr.Urls)
        {
            Check(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                $"Ssr.Urls entries must be absolute http or https URLs: '{url}'.");
        }

        foreach (var path in ssr.ExcludePaths)
        {
            Check(path is ['/', ..], $"Ssr.ExcludePaths entries must start with '/': '{path}'.");
        }

        Check(ssr.Concurrency.MaxActive >= 0, "Ssr.Concurrency.MaxActive must be 0 (unlimited) or positive.");
        Check(ssr.Concurrency.MaxQueued >= 0, "Ssr.Concurrency.MaxQueued must not be negative.");
        Check(IsDeadline(ssr.Concurrency.QueueTimeout, allowZero: true),
            "Ssr.Concurrency.QueueTimeout must be nonnegative and supported by CancellationTokenSource, or Timeout.InfiniteTimeSpan.");
        Check(ssr.CircuitBreaker.FailureThreshold >= 1, "Ssr.CircuitBreaker.FailureThreshold must be at least 1.");
        Check(ssr.CircuitBreaker.BreakDuration > TimeSpan.Zero, "Ssr.CircuitBreaker.BreakDuration must be positive.");
        if (ssr.NodeProcess is { } node)
        {
            Check(ssr.Urls.Count == 0, "Ssr.Urls is not used with Ssr.UseNodeProcess(): the managed instances listen on NodeProcess.BasePort and up.");
            Check(!string.IsNullOrWhiteSpace(node.Executable), "Ssr.NodeProcess.Executable must be set.");
            Check(node.Instances is >= 1 and <= 64, "Ssr.NodeProcess.Instances must be between 1 and 64.");
            Check(node.BasePort >= 1 && node.BasePort + Math.Max(node.Instances, 1) - 1 <= 65535,
                "Ssr.NodeProcess.BasePort must be a port such that every instance's port (BasePort + i) is at most 65535.");
            Check(IsDeadline(node.StartupTimeout, allowZero: false), "Ssr.NodeProcess.StartupTimeout must be positive and supported by CancellationTokenSource.");
            Check(node.LivenessInterval > TimeSpan.Zero && node.LivenessInterval.TotalMilliseconds <= uint.MaxValue - 1, "Ssr.NodeProcess.LivenessInterval must be positive.");
            Check(node.LivenessFailureThreshold >= 1, "Ssr.NodeProcess.LivenessFailureThreshold must be at least 1.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Check(bool valid, string failure)
        {
            if (!valid)
            {
                (failures ??= []).Add(failure);
            }
        }
    }

    private static bool IsDeadline(TimeSpan value, bool allowZero) => value == Timeout.InfiniteTimeSpan
        || ((allowZero ? value >= TimeSpan.Zero : value > TimeSpan.Zero) && value.TotalMilliseconds <= uint.MaxValue - 1);
}
