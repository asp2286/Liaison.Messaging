namespace Sample.Contracts;

public sealed record UploadDatasetRequest(
    string DatasetId,
    string Description,
    byte[] RawData);
