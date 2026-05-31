using Amazon;
using Amazon.S3;
using Liaison.Messaging;
using Liaison.Messaging.AwsStorage;
using Liaison.Messaging.AzureServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sample.Contracts;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();

var config = builder.Configuration;
var services = builder.Services;
var s3Client = new AmazonS3Client(
    config["S3:AccessKey"]!,
    config["S3:SecretKey"]!,
    RegionEndpoint.GetBySystemName(config["S3:Region"]!));

services.AddLiaisonMessagingDefaults();
services.AddAzureServiceBusClient(config["ServiceBus:ConnectionString"]!);
services.AddSingleton<IAmazonS3>(_ => s3Client);
services.AddSingleton<ILargePayloadPolicy>(_ =>
    new DefaultLargePayloadPolicy(
        new LargePayloadPolicyOptions(
            ThresholdBytes: 100 * 1024,
            UseCompression: false)));
services.AddS3PayloadStore(o =>
{
    o.Client = s3Client;
    o.BucketName = config["S3:BucketName"]!;
    o.Overwrite = true;
});
services.AddAzureServiceBusRequestClient<GenerateReportRequest, GenerateReportReply>(o =>
{
    o.RequestQueueName = config["ServiceBus:ReportRequestQueue"]!;
    o.ReplyQueueName = config["ServiceBus:ReportReplyQueue"]!;
    o.DefaultTimeout = TimeSpan.FromSeconds(30);
});
services.AddAzureServiceBusRequestClient<UploadDatasetRequest, UploadDatasetReply>(o =>
{
    o.RequestQueueName = config["ServiceBus:UploadDatasetRequestQueue"]!;
    o.ReplyQueueName = config["ServiceBus:UploadDatasetReplyQueue"]!;
    o.DefaultTimeout = TimeSpan.FromSeconds(60);
});

using var host = builder.Build();
var reportClient = host.Services.GetRequiredService<IRequestClient<GenerateReportRequest, GenerateReportReply>>();
var datasetClient = host.Services.GetRequiredService<IRequestClient<UploadDatasetRequest, UploadDatasetReply>>();

Console.WriteLine("Sending small request/reply message. This should stay inline in Azure Service Bus.");

var smallReply = await reportClient.SendAsync(
    new GenerateReportRequest(
        "SalesReport",
        new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero)));

switch (smallReply.Status)
{
    case ReplyStatus.Success:
        Console.WriteLine($"Small request succeeded: ReportId={smallReply.Value!.ReportId}");
        break;
    case ReplyStatus.Timeout:
        Console.WriteLine("Small request timed out.");
        break;
    default:
        Console.WriteLine($"Small request failed: {smallReply.Error}");
        break;
}

var rawData = new byte[300 * 1024];
Random.Shared.NextBytes(rawData);

Console.WriteLine("Sending large request/reply message (300KB). Threshold is 100KB, so large payload should use AWS S3.");

var largeReply = await datasetClient.SendAsync(
    new UploadDatasetRequest(
        "dataset-s3-demo",
        "300KB demo payload",
        rawData));

switch (largeReply.Status)
{
    case ReplyStatus.Success:
        Console.WriteLine(
            $"Large request succeeded: DatasetId={largeReply.Value!.DatasetId}, ProcessedBytes={largeReply.Value.ProcessedBytes}");
        break;
    case ReplyStatus.Timeout:
        Console.WriteLine("Large request timed out.");
        break;
    default:
        Console.WriteLine($"Large request failed: {largeReply.Error}");
        break;
}
