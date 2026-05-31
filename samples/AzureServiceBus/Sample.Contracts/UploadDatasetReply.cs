namespace Sample.Contracts;

public sealed record UploadDatasetReply(
    string DatasetId,
    string StorageHint,
    int ProcessedBytes);
