namespace Liaison.Messaging;

/// <summary>
/// Defines transport-neutral header names and canonical values for payload handling.
/// </summary>
public static class LargePayloadHeaders
{
    /// <summary>
    /// Header name for payload mode.
    /// </summary>
    public const string Mode = "liaison.payload.mode";

    /// <summary>
    /// Header name for payload reference.
    /// </summary>
    public const string Reference = "liaison.payload.ref";

    /// <summary>
    /// Header name for payload SHA-256 hash.
    /// </summary>
    public const string Sha256 = "liaison.payload.sha256";

    /// <summary>
    /// Header name for payload size in bytes.
    /// </summary>
    public const string Size = "liaison.payload.size";

    /// <summary>
    /// Header name for payload content encoding.
    /// </summary>
    public const string Encoding = "liaison.payload.encoding";

    /// <summary>
    /// Header name for payload expiry timestamp in UTC.
    /// </summary>
    public const string Expires = "liaison.payload.expires";

    /// <summary>
    /// Canonical mode value for inline payload bodies.
    /// </summary>
    public const string ModeInline = "inline";

    /// <summary>
    /// Canonical mode value for externally stored payload bodies.
    /// </summary>
    public const string ModeExternal = "external";

    /// <summary>
    /// Canonical encoding value for gzip-compressed payload bodies.
    /// </summary>
    public const string EncodingGzip = "gzip";
}
