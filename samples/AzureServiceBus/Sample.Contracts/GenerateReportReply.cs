namespace Sample.Contracts;

public sealed record GenerateReportReply(
    string ReportId,
    string DownloadUrl);
