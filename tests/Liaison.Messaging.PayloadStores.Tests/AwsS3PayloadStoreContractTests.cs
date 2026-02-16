namespace Liaison.Messaging.PayloadStores.Tests;

using Xunit;

public sealed class AwsS3PayloadStoreContractTests : PayloadStoreContractTests, IClassFixture<S3PayloadStoreFixture>
{
    public AwsS3PayloadStoreContractTests(S3PayloadStoreFixture fixture)
        : base(fixture)
    {
    }
}
