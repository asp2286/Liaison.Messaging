namespace Liaison.Messaging;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Returns a fixed timeout value for all requests.
/// </summary>
public sealed class FixedRequestTimeoutPolicy : IRequestTimeoutPolicy
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedRequestTimeoutPolicy"/> type.
    /// </summary>
    /// <param name="timeout">Timeout value. Use <see cref="Timeout.InfiniteTimeSpan"/> for no timeout.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is invalid.</exception>
    public FixedRequestTimeoutPolicy(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or infinite.");
        }

        _timeout = timeout;
    }

    /// <inheritdoc />
    public TimeSpan GetTimeout(IReadOnlyDictionary<string, string>? headers = null)
    {
        return _timeout;
    }
}
