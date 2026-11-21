using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LyricsPreferencesTests
{
    private const string KEY_AUTOSCROLL = "cantus_autoscroll";
    private const string KEY_KARAOKE = "cantus_karaoke";
    private const string KEY_LATENCY = "cantus_latency_ms";
    private const string KEY_POST_PROCESSING = "cantus_post_processing";
    private const int DEFAULT_LATENCY_MS = 200;
    private const int MAX_LATENCY_MS = 1000;
    private const int CUSTOM_LATENCY_MS = 350;
    private const int NON_BOOLEAN_VALUE = 42;

    [Fact]
    public void EmptyStore_ReturnsDefaults()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeTrue();
        preferences.IsKaraokeModeEnabled.Should().BeTrue();
        preferences.IsPostProcessingEnabled.Should().BeFalse();
        preferences.LatencyCompensationMs.Should().Be(DEFAULT_LATENCY_MS);
    }

    [Fact]
    public void WrittenValues_RoundTripThroughANewInstanceOverTheSameStore()
    {
        // Arrange
        InMemorySettingsStore store = new();
        LyricsPreferences writer = new(store);

        // Act
        writer.IsAutoScrollEnabled = false;
        writer.IsKaraokeModeEnabled = false;
        writer.IsPostProcessingEnabled = true;
        writer.LatencyCompensationMs = CUSTOM_LATENCY_MS;
        LyricsPreferences reader = new(store);

        // Assert
        reader.IsAutoScrollEnabled.Should().BeFalse();
        reader.IsKaraokeModeEnabled.Should().BeFalse();
        reader.IsPostProcessingEnabled.Should().BeTrue();
        reader.LatencyCompensationMs.Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void Values_ArePersistedUnderTheExistingStorageKeys()
    {
        // Arrange
        InMemorySettingsStore store = new();
        LyricsPreferences preferences = new(store);

        // Act
        preferences.IsAutoScrollEnabled = false;
        preferences.IsKaraokeModeEnabled = true;
        preferences.IsPostProcessingEnabled = true;
        preferences.LatencyCompensationMs = CUSTOM_LATENCY_MS;

        // Assert
        store.Values[KEY_AUTOSCROLL].Should().Be(false);
        store.Values[KEY_KARAOKE].Should().Be(true);
        store.Values[KEY_POST_PROCESSING].Should().Be(true);
        store.Values[KEY_LATENCY].Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void StringValues_AreParsed()
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_AUTOSCROLL, "False");
        store.SetValue(KEY_KARAOKE, "false");
        store.SetValue(KEY_POST_PROCESSING, "True");
        store.SetValue(KEY_LATENCY, "350");

        // Act
        LyricsPreferences preferences = new(store);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeFalse();
        preferences.IsKaraokeModeEnabled.Should().BeFalse();
        preferences.IsPostProcessingEnabled.Should().BeTrue();
        preferences.LatencyCompensationMs.Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void UnreadableValues_FallBackToDefaults()
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_AUTOSCROLL, "not-a-bool");
        store.SetValue(KEY_KARAOKE, NON_BOOLEAN_VALUE);
        store.SetValue(KEY_POST_PROCESSING, "sometimes");
        store.SetValue(KEY_LATENCY, "soon");

        // Act
        LyricsPreferences preferences = new(store);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeTrue();
        preferences.IsKaraokeModeEnabled.Should().BeTrue();
        preferences.IsPostProcessingEnabled.Should().BeFalse();
        preferences.LatencyCompensationMs.Should().Be(DEFAULT_LATENCY_MS);
    }

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(5000, MAX_LATENCY_MS)]
    [InlineData("-50", 0)]
    [InlineData("5000", MAX_LATENCY_MS)]
    public void StoredLatency_OutsideRange_IsClampedOnRead(object stored, int expected)
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_LATENCY, stored);

        // Act
        LyricsPreferences preferences = new(store);

        // Assert
        preferences.LatencyCompensationMs.Should().Be(expected);
    }

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(5000, MAX_LATENCY_MS)]
    public void WrittenLatency_OutsideRange_IsClampedBeforeStoring(int written, int expected)
    {
        // Arrange
        InMemorySettingsStore store = new();
        LyricsPreferences preferences = new(store);

        // Act
        preferences.LatencyCompensationMs = written;

        // Assert
        store.Values[KEY_LATENCY].Should().Be(expected);
        preferences.LatencyCompensationMs.Should().Be(expected);
    }
}
