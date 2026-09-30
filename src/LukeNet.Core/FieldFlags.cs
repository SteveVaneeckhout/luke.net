using System.Text;
using Lucene.Net.Index;

namespace LukeNet.Core;

/// <summary>
/// Builds a compact Luke style flag string for a field:
/// <c>I</c> indexed, <c>F</c> term frequencies, <c>P</c> positions, <c>O</c> offsets,
/// <c>V</c> term vectors, <c>N</c> norms, <c>Y</c> payloads, <c>D</c> doc values, <c>S</c> stored.
/// Missing properties are shown as <c>-</c>.
/// </summary>
public static class FieldFlags
{
    public const string Legend =
        "I = indexed, F = frequencies, P = positions, O = offsets, V = term vectors, " +
        "N = norms, Y = payloads, D = doc values, S = stored";

    public static string Describe(FieldInfo? info, bool stored)
    {
        var sb = new StringBuilder(9);
        var options = info?.IndexOptions ?? IndexOptions.NONE;
        sb.Append(info?.IsIndexed == true ? 'I' : '-');
        sb.Append(options >= IndexOptions.DOCS_AND_FREQS ? 'F' : '-');
        sb.Append(options >= IndexOptions.DOCS_AND_FREQS_AND_POSITIONS ? 'P' : '-');
        sb.Append(options >= IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS ? 'O' : '-');
        sb.Append(info?.HasVectors == true ? 'V' : '-');
        sb.Append(info?.HasNorms == true ? 'N' : '-');
        sb.Append(info?.HasPayloads == true ? 'Y' : '-');
        sb.Append(info?.HasDocValues == true ? 'D' : '-');
        sb.Append(stored ? 'S' : '-');
        return sb.ToString();
    }
}
