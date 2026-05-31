using Liaison.Messaging;
using Liaison.Messaging.AzureServiceBus;
using Liaison.Messaging.AzureStorage;
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

services.AddLiaisonMessagingDefaults();
services.AddAzureServiceBusClient(config["ServiceBus:ConnectionString"]!);
services.AddSingleton<ILargePayloadPolicy>(_ =>
    new DefaultLargePayloadPolicy(
        new LargePayloadPolicyOptions(
            ThresholdBytes: 100 * 1024,
            UseCompression: false)));
services.AddAzureBlobPayloadStore(o =>
{
    o.ConnectionString = config["BlobStorage:ConnectionString"]!;
    o.ContainerName = config["BlobStorage:ContainerName"]!;
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
