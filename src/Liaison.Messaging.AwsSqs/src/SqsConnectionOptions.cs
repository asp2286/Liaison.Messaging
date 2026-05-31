namespace Liaison.Messaging.AwsSqs;

using System;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;

/// <summary>
/// Defines explicit Amazon SQS client connection settings.
/// </summary>
public sealed record SqsConnectionOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqsConnectionOptions"/> type
    /// with default values for use with the <c>Action&lt;SqsConnectionOptions&gt;</c>
    /// configuration pattern.
    /// </summary>
    public SqsConnectionOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqsConnectionOptions"/> type.
    /// </summary>
    /// <param name="client">A preconfigured Amazon SQS client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public SqsConnectionOptions(IAmazonSQS client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Gets or sets a preconfigured Amazon SQS client. This is the preferred configuration path.
    /// </summary>
    public IAmazonSQS? Client { get; set; }

    /// <summary>
    /// Gets or sets optional AWS credentials for callers that construct a client outside the adapter.
    /// </summary>
    public AWSCredentials? Credentials { get; set; }

    /// <summary>
    /// Gets or sets an optional AWS region for callers that construct a client outside the adapter.
    /// </summary>
    public RegionEndpoint? RegionEndpoint { get; set; }

    /// <summary>
    /// Gets or sets an optional Amazon SQS client configuration for callers that construct a client outside the adapter.
    /// </summary>
    public AmazonSQSConfig? ClientConfig { get; set; }
}
