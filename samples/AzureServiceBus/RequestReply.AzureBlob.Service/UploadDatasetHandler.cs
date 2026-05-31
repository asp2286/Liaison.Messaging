using Liaison.Messaging;
using Sample.Contracts;

public sealed class UploadDatasetHandler
    : IRequestHandler<UploadDatasetRequest, UploadDatasetReply>
{
    public Task<UploadDatasetReply> HandleAsync(
        UploadDatasetRequest request,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[Service] Processing LARGE dataset request: DatasetId={request.DatasetId}, Bytes={request.RawData.Length:N0}");

        return Task.FromResult(
            new UploadDatasetReply(
                request.DatasetId,
                "AzureBlob",
                request.RawData.Length));
    }
}
