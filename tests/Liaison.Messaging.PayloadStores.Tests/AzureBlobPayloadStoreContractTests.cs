namespace Liaison.Messaging.PayloadStores.Tests;

using Xunit;

public sealed class AzureBlobPayloadStoreContractTests : PayloadStoreContractTests, IClassFixture<AzureBlobPayloadStoreFixture>
{
    public AzureBlobPayloadStoreContractTests(AzureBlobPayloadStoreFixture fixture)
        : base(fixture)
    {
    }
}
