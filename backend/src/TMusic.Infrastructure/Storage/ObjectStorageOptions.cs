namespace TMusic.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public string? Endpoint { get; set; }
    public string? Bucket { get; set; }
    public string Region { get; set; } = "auto";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Prefix { get; set; } = "tmusic/audio";
    public bool PathStyle { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
}
