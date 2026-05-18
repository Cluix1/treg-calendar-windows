namespace TregCalendar.Remote;

public sealed record NativeSyncClientOptions
{
    public Uri FunctionUri { get; init; } = new("https://havdnzhsaxefoehtoyna.supabase.co/functions/v1/sync-native-calendar");
}
