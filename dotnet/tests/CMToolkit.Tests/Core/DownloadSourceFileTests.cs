using System.Text;
using CMToolkit.Core;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core;

/// <summary>
/// SET-2: <c>assets\download-source.txt</c> is read as UTF-8 with errors ignored, stripped, and must be
/// <c>nexus</c> or <c>github</c>; anything else, or a read failure, becomes <c>nexus</c>
/// (<c>app_settings.py:31-42</c> at the Parity Baseline). It is resolved against the exe folder (ADR-0002).
/// </summary>
[Trait("Parity", "SET-2")]
public sealed class DownloadSourceFileTests : IDisposable
{
    private readonly TempDirectory _appDir = new();

    public void Dispose() => _appDir.Dispose();

    private DownloadSourceLookup ReadWith(string text) => ReadWith(Encoding.UTF8.GetBytes(text));

    private DownloadSourceLookup ReadWith(byte[] bytes)
    {
        _appDir.WriteBytes(Path.Combine("assets", "download-source.txt"), bytes);
        return DownloadSourceFile.Read(_appDir.Path);
    }

    [Theory]
    [InlineData("github", DownloadSource.GitHub)]
    [InlineData("nexus", DownloadSource.Nexus)]
    public void A_valid_value_is_used(string text, DownloadSource expected)
    {
        var lookup = ReadWith(text);

        Assert.Equal(expected, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.Valid, lookup.Outcome);
        Assert.Equal(text, lookup.RawValue);
        Assert.Null(lookup.Error);
    }

    [Theory]
    [InlineData("github\r\n")]
    [InlineData("  github\t\n")]
    // Python's str.strip() also removes U+001C..U+001F (file/group/record/unit separators), which .NET's
    // char.IsWhiteSpace does not count as whitespace.
    [InlineData("\u001cgithub\u001f")]
    [InlineData("　github ")]
    public void Surrounding_whitespace_is_stripped_as_python_strips_it(string text)
    {
        var lookup = ReadWith(text);

        Assert.Equal(DownloadSource.GitHub, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.Valid, lookup.Outcome);
        Assert.Equal("github", lookup.RawValue);
    }

    [Fact]
    public void Invalid_utf8_bytes_are_dropped_rather_than_replaced()
    {
        var lookup = ReadWith([.. "git"u8, 0xFF, .. "hub"u8]);

        Assert.Equal(DownloadSource.GitHub, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.Valid, lookup.Outcome);
    }

    [Fact]
    public void A_utf8_bom_is_kept_and_makes_the_value_invalid()
    {
        // The reference decodes with "utf-8", not "utf-8-sig", and U+FEFF isn't whitespace, so a BOM survives strip().
        var lookup = ReadWith([0xEF, 0xBB, 0xBF, .. "github"u8]);

        Assert.Equal(DownloadSource.Nexus, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.Invalid, lookup.Outcome);
        Assert.Equal("﻿github", lookup.RawValue);
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("GitHub")]
    [InlineData("both")]
    [InlineData("")]
    public void Any_other_value_becomes_nexus(string text)
    {
        var lookup = ReadWith(text);

        Assert.Equal(DownloadSource.Nexus, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.Invalid, lookup.Outcome);
        Assert.Equal(text, lookup.RawValue);
        Assert.Null(lookup.Error);
    }

    [Fact]
    public void A_missing_file_becomes_nexus()
    {
        var lookup = DownloadSourceFile.Read(_appDir.Path);

        Assert.Equal(DownloadSource.Nexus, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.ReadFailed, lookup.Outcome);
        Assert.Null(lookup.RawValue);
        Assert.NotNull(lookup.Error);
    }

    [Fact]
    public void A_directory_in_place_of_the_file_becomes_nexus()
    {
        Directory.CreateDirectory(Path.Combine(_appDir.Path, "assets", "download-source.txt"));

        var lookup = DownloadSourceFile.Read(_appDir.Path);

        Assert.Equal(DownloadSource.Nexus, lookup.Source);
        Assert.Equal(DownloadSourceOutcome.ReadFailed, lookup.Outcome);
        Assert.NotNull(lookup.Error);
    }

    [Fact]
    public void The_file_is_resolved_against_the_given_app_directory_not_the_working_directory()
    {
        Assert.Equal(
            Path.Combine(_appDir.Path, "assets", "download-source.txt"),
            DownloadSourceFile.PathIn(_appDir.Path));
    }
}
