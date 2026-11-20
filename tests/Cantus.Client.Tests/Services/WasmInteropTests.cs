using System;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class WasmInteropTests
{
    [Fact]
    public void GetCurrentOrigin_OutsideWasm_ReturnsEmptyString()
    {
        string origin = WasmInterop.GetCurrentOrigin();

        origin.Should().Be(string.Empty);
    }

    [Fact]
    public void NavigateTo_OutsideWasm_DoesNotThrow()
    {
        Action act = () => WasmInterop.NavigateTo("https://example.com");

        act.Should().NotThrow();
    }

    [Fact]
    public void GetAuthQueryParameter_OutsideWasm_ReturnsEmptyString()
    {
        string auth = WasmInterop.GetAuthQueryParameter();

        auth.Should().Be(string.Empty);
    }

    [Fact]
    public void GetLocationSearch_OutsideWasm_ReturnsEmptyString()
    {
        string search = WasmInterop.GetLocationSearch();

        search.Should().Be(string.Empty);
    }

    [Fact]
    public void RemoveQueryParameter_OutsideWasm_DoesNotThrow()
    {
        Action act = () => WasmInterop.RemoveQueryParameter("pair");

        act.Should().NotThrow();
    }

    [Fact]
    public void CleanAuthQuery_OutsideWasm_DoesNotThrow()
    {
        Action act = () => WasmInterop.CleanAuthQuery();

        act.Should().NotThrow();
    }

    [Fact]
    public void IsDocumentVisible_OutsideWasm_ReturnsTrue()
    {
        bool isVisible = WasmInterop.IsDocumentVisible();

        isVisible.Should().BeTrue();
    }
}
