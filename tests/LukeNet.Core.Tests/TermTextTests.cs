namespace LukeNet.Core.Tests;

public sealed class TermTextTests
{
    [Fact]
    public void Utf8Text_IsReturnedAsIs() =>
        Assert.Equal("héllo", TermText.ToDisplay("héllo"u8.ToArray()));

    [Fact]
    public void InvalidUtf8_IsHex() =>
        Assert.Equal("0xFFFE", TermText.ToDisplay([0xFF, 0xFE]));

    [Fact]
    public void ControlCharacters_AreHex() =>
        Assert.Equal("0x600801", TermText.ToDisplay([0x60, 0x08, 0x01]));
}
