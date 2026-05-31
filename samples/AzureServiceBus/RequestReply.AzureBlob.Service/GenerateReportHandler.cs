using Liaison.Messaging;
using Sample.Contracts;

public sealed class GenerateReportHandler
    : IRequestHandler<GenerateReportRequest, GenerateReportReply>
{
    public Task<GenerateReportReply> HandleAsync(
        GenerateReportRequest request,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[Service] Processing small report request: {request.ReportType} {request.FromUtc:yyyy-MM-dd} to {request.ToUtc:yyyy-MM-dd}");

        return Task.FromResult(
            new GenerateReportReply(
                Guid.NewGuid().ToString("N"),
                $"https://example.com/reports/{request.ReportType.ToLowerInvariant()}"));
    }
}
