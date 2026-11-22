namespace Api.Tests;

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan duration) => now += duration;

    public void SetUtcNow(DateTimeOffset value) => now = value;
}
