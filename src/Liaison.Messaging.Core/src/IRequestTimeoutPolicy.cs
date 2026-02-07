namespace Liaison.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Resolves a request timeout for request/reply operations.
/// </summary>
public interface IRequestTimeoutPolicy
{
    /// <summary>
    /// Gets the timeout to apply for the request.
    /// </summary>
    /// <param name="headers">Optional request headers.</param>
    /// <returns>The timeout value.</returns>
    TimeSpan GetTimeout(IReadOnlyDictionary<string, string>? headers = null);
}
