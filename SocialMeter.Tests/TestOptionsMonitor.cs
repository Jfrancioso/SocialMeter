using Microsoft.Extensions.Options;

namespace SocialMeter.Tests;

/// <summary>An IOptionsMonitor that always returns the same value, for services that read live options.</summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
