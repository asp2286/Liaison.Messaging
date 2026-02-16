namespace Liaison.Messaging.PayloadStores.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liaison.Messaging;
using Xunit;

public abstract class PayloadStoreFixture : IAsyncLifetime
{
    public abstract string ProviderName { get; }

    public abstract bool IsEnabled { get; }

    public abstract bool SupportsConditionalPut { get; }

    public abstract bool CanDisableConditionalPut { get; }

    public abstract bool CanVerifyExpiresMarker { get; }

    public virtual string CreateReferencePrefix()
    {
        return $"contract/{Guid.NewGuid():N}";
    }

    public abstract IPayloadStore CreateStore(
        bool overwrite = false,
        bool emitExpiresMarker = true,
        bool supportsConditionalPut = true);

    public abstract Task<string?> ReadExpiresMarkerAsync(string reference, CancellationToken ct = default);

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
