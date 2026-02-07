var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TODO: Wire Liaison.Messaging minimal API sample.");

app.Run();
