using Cantus.Client.Models;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ClientPreferencesTests
{
    private const string KEY_THEME = "cantus_theme";
    private const string KEY_AUTOSCROLL = "cantus_autoscroll";
    private const string KEY_KARAOKE = "cantus_karaoke";
    private const string KEY_LATENCY = "cantus_latency_ms";
    private const string KEY_FILM_GRAIN = "cantus_fx_film_grain";
    private const string KEY_VIGNETTE = "cantus_fx_vignette";
    private const string KEY_SCANLINES = "cantus_fx_scanlines";
    private const string KEY_BACKGROUND_BLUR = "cantus_fx_background_blur";
    private const int DEFAULT_LATENCY_MS = 200;
    private const int MAX_LATENCY_MS = 1000;
    private const int CUSTOM_LATENCY_MS = 350;
    private const int NON_BOOLEAN_VALUE = 42;

    [Fact]
    public void EmptyStore_ReturnsDefaults()
    {
        // Arrange
        ClientPreferences preferences = new(new InMemorySettingsStore());

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeTrue();
        preferences.IsKaraokeModeEnabled.Should().BeTrue();
        preferences.Theme.Should().Be(ThemeMode.Dynamic);
        preferences.IsFilmGrainEnabled.Should().BeTrue();
        preferences.HasFilmGrainChoice.Should().BeFalse();
        preferences.IsVignetteEnabled.Should().BeFalse();
        preferences.IsScanlinesEnabled.Should().BeFalse();
        preferences.IsBackgroundBlurEnabled.Should().BeTrue();
        preferences.IsVuMeterEnabled.Should().BeTrue();
        preferences.VuMeterSource.Should().BeNull();
        preferences.LatencyCompensationMs.Should().Be(DEFAULT_LATENCY_MS);
    }

    [Theory]
    [InlineData(VuMeterSource.None)]
    [InlineData(VuMeterSource.SharedAudio)]
    public void VuMeterSettings_RoundTripUnderTheirKeys(VuMeterSource source)
    {
        // Arrange
        InMemorySettingsStore store = new();
        ClientPreferences writer = new(store);

        // Act
        writer.IsVuMeterEnabled = false;
        writer.VuMeterSource = source;
        ClientPreferences reader = new(store);

        // Assert
        reader.IsVuMeterEnabled.Should().BeFalse();
        reader.VuMeterSource.Should().Be(source);
        store.Values["cantus_fx_vu_meter"].Should().Be(false);
        store.Values["cantus_vu_meter_source"].Should().Be(source.ToString());
    }

    [Fact]
    public void VuMeterSource_SetToNull_ReadsAsNull()
    {
        // Arrange
        ClientPreferences preferences = new(new InMemorySettingsStore()) { VuMeterSource = VuMeterSource.Microphone };

        // Act
        preferences.VuMeterSource = null;

        // Assert
        preferences.VuMeterSource.Should().BeNull();
    }

    [Theory]
    [InlineData("Theremin")]
    [InlineData("Decorative")]
    [InlineData("99")]
    [InlineData(NON_BOOLEAN_VALUE)]
    public void VuMeterSource_UnknownStoredValue_ReadsAsNull(object stored)
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue("cantus_vu_meter_source", stored);

        // Act / Assert
        new ClientPreferences(store).VuMeterSource.Should().BeNull();
    }

    [Fact]
    public void WrittenValues_RoundTripThroughANewInstanceOverTheSameStore()
    {
        // Arrange
        InMemorySettingsStore store = new();
        ClientPreferences writer = new(store);

        // Act
        writer.IsAutoScrollEnabled = false;
        writer.IsKaraokeModeEnabled = false;
        writer.Theme = ThemeMode.NordicSlate;
        writer.IsFilmGrainEnabled = false;
        writer.IsVignetteEnabled = true;
        writer.IsScanlinesEnabled = true;
        writer.IsBackgroundBlurEnabled = false;
        writer.LatencyCompensationMs = CUSTOM_LATENCY_MS;
        ClientPreferences reader = new(store);

        // Assert
        reader.IsAutoScrollEnabled.Should().BeFalse();
        reader.IsKaraokeModeEnabled.Should().BeFalse();
        reader.Theme.Should().Be(ThemeMode.NordicSlate);
        reader.IsFilmGrainEnabled.Should().BeFalse();
        reader.HasFilmGrainChoice.Should().BeTrue();
        reader.IsVignetteEnabled.Should().BeTrue();
        reader.IsScanlinesEnabled.Should().BeTrue();
        reader.IsBackgroundBlurEnabled.Should().BeFalse();
        reader.LatencyCompensationMs.Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void Values_ArePersistedUnderTheExistingStorageKeys()
    {
        // Arrange
        InMemorySettingsStore store = new();
        ClientPreferences preferences = new(store);

        // Act
        preferences.IsAutoScrollEnabled = false;
        preferences.IsKaraokeModeEnabled = true;
        preferences.Theme = ThemeMode.SolarizedDark;
        preferences.IsFilmGrainEnabled = true;
        preferences.IsVignetteEnabled = false;
        preferences.IsScanlinesEnabled = true;
        preferences.IsBackgroundBlurEnabled = true;
        preferences.LatencyCompensationMs = CUSTOM_LATENCY_MS;

        // Assert
        store.Values[KEY_AUTOSCROLL].Should().Be(false);
        store.Values[KEY_KARAOKE].Should().Be(true);
        store.Values[KEY_THEME].Should().Be("SolarizedDark");
        store.Values[KEY_FILM_GRAIN].Should().Be(true);
        store.Values[KEY_VIGNETTE].Should().Be(false);
        store.Values[KEY_SCANLINES].Should().Be(true);
        store.Values[KEY_BACKGROUND_BLUR].Should().Be(true);
        store.Values[KEY_LATENCY].Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void StringValues_AreParsed()
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_AUTOSCROLL, "False");
        store.SetValue(KEY_KARAOKE, "false");
        store.SetValue(KEY_FILM_GRAIN, "False");
        store.SetValue(KEY_VIGNETTE, "true");
        store.SetValue(KEY_SCANLINES, "TRUE");
        store.SetValue(KEY_BACKGROUND_BLUR, "false");
        store.SetValue(KEY_LATENCY, "350");

        // Act
        ClientPreferences preferences = new(store);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeFalse();
        preferences.IsKaraokeModeEnabled.Should().BeFalse();
        preferences.IsFilmGrainEnabled.Should().BeFalse();
        preferences.HasFilmGrainChoice.Should().BeTrue();
        preferences.IsVignetteEnabled.Should().BeTrue();
        preferences.IsScanlinesEnabled.Should().BeTrue();
        preferences.IsBackgroundBlurEnabled.Should().BeFalse();
        preferences.LatencyCompensationMs.Should().Be(CUSTOM_LATENCY_MS);
    }

    [Fact]
    public void UnreadableValues_FallBackToDefaults()
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_AUTOSCROLL, "not-a-bool");
        store.SetValue(KEY_KARAOKE, NON_BOOLEAN_VALUE);
        store.SetValue(KEY_FILM_GRAIN, "sometimes");
        store.SetValue(KEY_VIGNETTE, NON_BOOLEAN_VALUE);
        store.SetValue(KEY_SCANLINES, "1");
        store.SetValue(KEY_BACKGROUND_BLUR, "yes");
        store.SetValue(KEY_LATENCY, "soon");

        // Act
        ClientPreferences preferences = new(store);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeTrue();
        preferences.IsKaraokeModeEnabled.Should().BeTrue();
        preferences.IsFilmGrainEnabled.Should().BeTrue();
        preferences.HasFilmGrainChoice.Should().BeFalse();
        preferences.IsVignetteEnabled.Should().BeFalse();
        preferences.IsScanlinesEnabled.Should().BeFalse();
        preferences.IsBackgroundBlurEnabled.Should().BeTrue();
        preferences.LatencyCompensationMs.Should().Be(DEFAULT_LATENCY_MS);
    }

    [Theory]
    [InlineData("emeraldsynth", ThemeMode.EmeraldSynth)]
    [InlineData("OLEDMonochrome", ThemeMode.OLEDMonochrome)]
    [InlineData("RetiredTheme", ThemeMode.Dynamic)]
    [InlineData("99", ThemeMode.Dynamic)]
    [InlineData("", ThemeMode.Dynamic)]
    public void StoredTheme_IsParsedByName_AndUnknownValuesFallBackToDynamic(string stored, ThemeMode expected)
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_THEME, stored);

        // Act
        ClientPreferences preferences = new(store);

        // Assert
        preferences.Theme.Should().Be(expected);
    }

    [Fact]
    public void StoredTheme_OfTheWrongType_FallsBackToDynamic()
    {
        // Arrange
        InMemorySettingsStore store = new();
        store.SetValue(KEY_THEME, NON_BOOLEAN_VALUE);

        // Act
        ClientPreferences preferences = new(store);

        // Assert
        preferences.Theme.Should().Be(ThemeMode.Dynamic);
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
        ClientPreferences preferences = new(store);

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
        ClientPreferences preferences = new(store);

        // Act
        preferences.LatencyCompensationMs = written;

        // Assert
        store.Values[KEY_LATENCY].Should().Be(expected);
        preferences.LatencyCompensationMs.Should().Be(expected);
    }
}
