using Liaison.Messaging;
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

services.AddSingleton<IMessageSerializer, SampleMessageSerializer>();
services.AddSingleton<IMessageIdGenerator, GuidMessageIdGenerator>();
services.AddSingleton<IMessageEnvelopeFactory, MessageEnvelopeFactory>();
services.AddSingleton<IMessageContextFactory, MessageContextFactory>();
services.AddAzureServiceBusClient(config["ServiceBus:ConnectionString"]!);
services.AddAzureServiceBusSubscription<UserRegistered, UserRegisteredHandler>(o =>
{
    o.EntityName = config["ServiceBus:UserEventsTopic"]!;
    o.Kind = AzureServiceBusEntityKind.Topic;
    o.SubscriptionName = config["ServiceBus:UserEventsSubscription"]!;
});
services.AddAzureServiceBusSubscriptionService<UserRegistered>();

using var host = builder.Build();
host.Run();
