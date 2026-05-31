namespace Sample.Contracts;

public sealed record GenerateReportRequest(
    string ReportType,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);
