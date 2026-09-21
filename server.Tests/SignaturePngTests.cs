using Server.Services;

namespace Server.Tests;

public sealed class SignaturePngTests
{
    private static bool Parse(string? dataUrl, out byte[] png, out string error) =>
        SignaturePng.TryParse(dataUrl, out png, out error);

    [Fact]
    public void A_well_formed_png_data_url_is_accepted_and_decoded()
    {
        var bytes = TestPng.Build();

        Assert.True(Parse(TestPng.DataUrl(bytes), out var png, out var error), error);
        Assert.Equal(bytes, png);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_signature_is_rejected(string? dataUrl)
    {
        Assert.False(Parse(dataUrl, out _, out var error));
        Assert.Contains("Sign", error);
    }

    [Theory]
    [InlineData("http://example.com/sig.png")]
    [InlineData("data:image/jpeg;base64,AAAA")]
    [InlineData("data:image/png;base64")]
    public void Anything_that_is_not_a_base64_png_data_url_is_rejected(string dataUrl)
    {
        Assert.False(Parse(dataUrl, out _, out _));
    }

    [Fact]
    public void Invalid_base64_is_rejected()
    {
        Assert.False(Parse("data:image/png;base64,!!!not base64!!!", out _, out _));
    }

    [Fact]
    public void Bytes_that_are_not_a_png_are_rejected()
    {
        var notPng = "data:image/png;base64," + Convert.ToBase64String(new byte[500]);

        Assert.False(Parse(notPng, out _, out _));
    }

    [Fact]
    public void A_png_without_its_closing_chunk_is_rejected()
    {
        Assert.False(Parse(TestPng.DataUrl(TestPng.Build(iend: false)), out _, out _));
    }

    [Fact]
    public void A_png_without_image_data_is_rejected()
    {
        Assert.False(Parse(TestPng.DataUrl(TestPng.Build(idat: false)), out _, out _));
    }

    [Theory]
    [InlineData(SignaturePng.MinWidth - 1, 320)]
    [InlineData(SignaturePng.MaxWidth + 1, 320)]
    [InlineData(1000, SignaturePng.MinHeight - 1)]
    [InlineData(1000, SignaturePng.MaxHeight + 1)]
    public void A_size_outside_the_allowed_range_is_rejected(int width, int height)
    {
        Assert.False(Parse(TestPng.DataUrl(TestPng.Build(width, height)), out _, out var error));
        Assert.Contains("size", error);
    }

    [Theory]
    [InlineData(SignaturePng.MinWidth, SignaturePng.MinHeight)]
    [InlineData(SignaturePng.MaxWidth, SignaturePng.MaxHeight)]
    public void The_edges_of_the_allowed_range_are_accepted(int width, int height)
    {
        Assert.True(Parse(TestPng.DataUrl(TestPng.Build(width, height)), out _, out var error), error);
    }

    [Fact]
    public void An_image_over_the_size_cap_is_rejected()
    {
        var tooBig = TestPng.Build(dataBytes: SignaturePng.MaxBytes);

        Assert.False(Parse(TestPng.DataUrl(tooBig), out _, out var error));
        Assert.Contains("large", error);
    }

    [Fact]
    public void An_image_just_under_the_size_cap_is_accepted()
    {
        // Filler + the ~70 bytes of PNG framing has to stay within MaxBytes.
        var fits = TestPng.Build(dataBytes: SignaturePng.MaxBytes - 100);

        Assert.True(Parse(TestPng.DataUrl(fits), out _, out var error), error);
    }
}
