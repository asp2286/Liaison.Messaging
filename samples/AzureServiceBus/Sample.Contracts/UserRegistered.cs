namespace Sample.Contracts;

public sealed record UserRegistered(
    string UserId,
    string Email,
    DateTimeOffset RegisteredAtUtc);
