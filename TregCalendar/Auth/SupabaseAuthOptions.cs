namespace TregCalendar.Auth;

public sealed record SupabaseAuthOptions
{
    public Uri SupabaseUrl { get; init; } = new("https://havdnzhsaxefoehtoyna.supabase.co/");

    public string PublishableKey { get; init; } =
        Environment.GetEnvironmentVariable("TREG_SUPABASE_PUBLISHABLE_KEY")
        ?? Environment.GetEnvironmentVariable("VITE_SUPABASE_PUBLISHABLE_KEY")
        ?? string.Empty;

    public bool HasPublishableKey => !string.IsNullOrWhiteSpace(PublishableKey);
}
