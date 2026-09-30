using Lucene.Net.Search;

namespace LukeNet.Core;

/// <summary>General information about an opened index.</summary>
public sealed record IndexOverview(
    string Path,
    int NumDocs,
    int MaxDoc,
    int DeletedDocs,
    long Version,
    long Generation,
    string SegmentsFile,
    int SegmentCount,
    long SizeInBytes,
    IReadOnlyList<string> Files,
    IReadOnlyDictionary<string, string> UserData);

/// <summary>Per-field information merged over all segments.</summary>
public sealed record FieldSummary(
    string Name,
    int Number,
    bool Indexed,
    string IndexOptions,
    bool HasNorms,
    bool HasTermVectors,
    bool HasPayloads,
    string DocValues,
    long TermCount,
    int DocCount);

/// <summary>Information about a single segment of the latest commit.</summary>
public sealed record SegmentSummary(
    string Name,
    int DocCount,
    int DeletedDocs,
    string Codec,
    string LuceneVersion,
    bool CompoundFile,
    long SizeInBytes);

/// <summary>A term of a field together with its statistics.</summary>
/// <param name="Text">Human readable representation (UTF-8 text, or hex when the term is binary).</param>
/// <param name="Bytes">The raw term bytes, usable to build an exact term query.</param>
public sealed record TermInfo(string Field, string Text, byte[] Bytes, int DocFreq, long TotalTermFreq);

/// <summary>A stored field value of a document.</summary>
/// <param name="Kind">One of "string", "numeric" or "binary".</param>
/// <param name="Flags">Luke style flags of the field as known by the index (see <see cref="FieldFlags"/>).</param>
public sealed record StoredFieldValue(string Name, string Value, string Kind, string Flags);

public sealed record DocumentView(int DocId, bool IsDeleted, IReadOnlyList<StoredFieldValue> Fields);

public sealed record SearchHit(int DocId, float Score, string Summary);

public sealed record SearchResult(Query Query, string ParsedQuery, int TotalHits, IReadOnlyList<SearchHit> Hits, TimeSpan Elapsed);
