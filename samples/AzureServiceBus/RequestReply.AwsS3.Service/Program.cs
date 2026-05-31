using Amazon;
using Amazon.S3;
using Liaison.Messaging;
using Liaison.Messaging.AwsStorage;
using Liaison.Messaging.AzureServiceBus;
using Liaison.Messaging.Hosting;
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
services.AddAzureServiceBusRequestProcessor<GenerateReportRequest, GenerateReportReply, GenerateReportHandler>(o =>
{
    o.RequestQueueName = config["ServiceBus:ReportRequestQueue"]!;
    o.ReplyQueueName = config["ServiceBus:ReportReplyQueue"]!;
    o.DefaultTimeout = TimeSpan.FromSeconds(30);
});
services.AddAzureServiceBusRequestProcessorService<GenerateReportRequest, GenerateReportReply>();
services.AddAzureServiceBusRequestProcessor<UploadDatasetRequest, UploadDatasetReply, UploadDatasetHandler>(o =>
{
    o.RequestQueueName = config["ServiceBus:UploadDatasetRequestQueue"]!;
    o.ReplyQueueName = config["ServiceBus:UploadDatasetReplyQueue"]!;
    o.DefaultTimeout = TimeSpan.FromSeconds(60);
});
services.AddAzureServiceBusRequestProcessorService<UploadDatasetRequest, UploadDatasetReply>();

using var host = builder.Build();
host.Run();
