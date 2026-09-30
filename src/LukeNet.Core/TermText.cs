using System.Text;
using Lucene.Net.Util;

namespace LukeNet.Core;

/// <summary>Converts raw term bytes into something that can be shown to a user.</summary>
public static class TermText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Returns the term as text when it is valid, printable UTF-8; otherwise returns a hex dump
    /// prefixed with <c>0x</c> (e.g. numeric trie terms or binary terms).
    /// </summary>
    public static string ToDisplay(BytesRef bytes) => ToDisplay(bytes.Bytes, bytes.Offset, bytes.Length);

    public static string ToDisplay(byte[] bytes) => ToDisplay(bytes, 0, bytes.Length);

    public static string ToDisplay(byte[] bytes, int offset, int length)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes, offset, length);
            if (!text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
                return text;
        }
        catch (DecoderFallbackException)
        {
        }
        return "0x" + Convert.ToHexString(bytes, offset, length);
    }
}
