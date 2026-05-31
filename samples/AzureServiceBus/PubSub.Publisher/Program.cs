using Liaison.Messaging;
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

services.AddSingleton<IMessageSerializer, SampleMessageSerializer>();
services.AddSingleton<IMessageIdGenerator, GuidMessageIdGenerator>();
services.AddSingleton<IMessageEnvelopeFactory, MessageEnvelopeFactory>();
services.AddAzureServiceBusClient(config["ServiceBus:ConnectionString"]!);
services.AddAzureServiceBusPublisher<UserRegistered>(o =>
{
    o.EntityName = config["ServiceBus:UserEventsTopic"]!;
    o.Kind = AzureServiceBusEntityKind.Topic;
});

using var host = builder.Build();
var publisher = host.Services.GetRequiredService<IMessagePublisher<UserRegistered>>();

var message = new UserRegistered(
    "user-1001",
    "ada.lovelace@example.com",
    new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

await publisher.PublishAsync(message);

Console.WriteLine($"Published UserRegistered for user {message.UserId}");
