using System.Diagnostics;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using LuceneDirectory = Lucene.Net.Store.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;

namespace LukeNet.Core;

/// <summary>
/// Read-only access to a Lucene (4.x) index: overview, fields, terms, documents and search.
/// The instance is safe to use from multiple threads.
/// </summary>
public sealed class IndexInspector : IDisposable
{
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    /// <summary>Analyzers that can be used to parse search queries.</summary>
    public static IReadOnlyList<string> AnalyzerNames { get; } = ["Standard", "Whitespace", "Simple", "Keyword"];

    private readonly LuceneDirectory _directory;
    private readonly bool _ownsDirectory;
    private readonly DirectoryReader _reader;
    private readonly IndexSearcher _searcher;
    private readonly FieldInfos _fieldInfos;
    private readonly IBits? _liveDocs;

    private IndexInspector(string path, LuceneDirectory directory, bool ownsDirectory)
    {
        Path = path;
        _directory = directory;
        _ownsDirectory = ownsDirectory;
        _reader = DirectoryReader.Open(directory);
        _searcher = new IndexSearcher(_reader);
        _fieldInfos = MultiFields.GetMergedFieldInfos(_reader);
        _liveDocs = MultiFields.GetLiveDocs(_reader);
    }

    public string Path { get; }

    public int MaxDoc => _reader.MaxDoc;

    public int NumDocs => _reader.NumDocs;

    /// <summary>Opens the index stored in the given file system directory.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="InvalidDataException">The directory does not contain a Lucene index.</exception>
    public static IndexInspector Open(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory '{fullPath}' does not exist.");

        var directory = FSDirectory.Open(fullPath);
        try
        {
            if (!DirectoryReader.IndexExists(directory))
                throw new InvalidDataException($"No Lucene index found in '{fullPath}'.");
            return new IndexInspector(fullPath, directory, ownsDirectory: true);
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    /// <summary>Opens an index from an existing Lucene directory (which stays owned by the caller).</summary>
    public static IndexInspector Open(LuceneDirectory directory, string displayName = "(in memory)") =>
        new(displayName, directory, ownsDirectory: false);

    public IndexOverview GetOverview()
    {
        var commit = _reader.IndexCommit;
        var files = commit.FileNames.OrderBy(f => f, StringComparer.Ordinal).ToList();
        return new IndexOverview(
            Path,
            _reader.NumDocs,
            _reader.MaxDoc,
            _reader.NumDeletedDocs,
            _reader.Version,
            commit.Generation,
            commit.SegmentsFileName,
            commit.SegmentCount,
            files.Sum(FileLength),
            files,
            new Dictionary<string, string>(commit.UserData));
    }

    public IReadOnlyList<FieldSummary> GetFields()
    {
        var result = new List<FieldSummary>();
        foreach (var info in _fieldInfos.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var terms = info.IsIndexed ? MultiFields.GetTerms(_reader, info.Name) : null;
            result.Add(new FieldSummary(
                info.Name,
                info.Number,
                info.IsIndexed,
                info.IsIndexed ? info.IndexOptions.ToString() : "",
                info.HasNorms,
                info.HasVectors,
                info.HasPayloads,
                info.HasDocValues ? info.DocValuesType.ToString() : "",
                terms is null ? 0 : CountTerms(terms),
                terms?.DocCount ?? 0));
        }
        return result;
    }

    public IReadOnlyList<SegmentSummary> GetSegments()
    {
        var infos = new SegmentInfos();
        infos.Read(_directory, _reader.IndexCommit.SegmentsFileName);
        var result = new List<SegmentSummary>();
        foreach (var commitInfo in infos.Segments)
        {
            var info = commitInfo.Info;
            result.Add(new SegmentSummary(
                info.Name,
                info.DocCount,
                commitInfo.DelCount,
                info.Codec?.Name ?? "",
                info.Version ?? "",
                info.UseCompoundFile,
                commitInfo.GetSizeInBytes()));
        }
        return result;
    }

    /// <summary>Names of all indexed fields, sorted alphabetically.</summary>
    public IReadOnlyList<string> GetIndexedFieldNames() =>
        _fieldInfos.Where(f => f.IsIndexed).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Returns the terms with the highest document frequency. When <paramref name="field"/> is null all
    /// indexed fields are considered. Note that deleted documents are still counted until segments merge.
    /// </summary>
    public IReadOnlyList<TermInfo> GetTopTerms(string? field, int count)
    {
        if (count <= 0)
            return [];

        // Min-heap on doc freq: the root is the weakest of the current top terms.
        var heap = new PriorityQueue<(string Field, byte[] Bytes, int DocFreq, long TotalTermFreq), int>();
        var fields = field is null ? GetIndexedFieldNames() : [field];
        foreach (var name in fields)
        {
            var terms = MultiFields.GetTerms(_reader, name);
            if (terms is null)
                continue;
            var termsEnum = terms.GetEnumerator();
            while (termsEnum.MoveNext())
            {
                var docFreq = termsEnum.DocFreq;
                if (heap.Count == count)
                {
                    heap.TryPeek(out _, out var weakest);
                    if (docFreq <= weakest)
                        continue;
                    heap.Dequeue();
                }
                heap.Enqueue((name, CopyBytes(termsEnum.Term), docFreq, termsEnum.TotalTermFreq), docFreq);
            }
        }

        return heap.UnorderedItems
            .Select(x => x.Element)
            .OrderByDescending(t => t.DocFreq)
            .ThenBy(t => t.Field, StringComparer.Ordinal)
            .Select(t => new TermInfo(t.Field, TermText.ToDisplay(t.Bytes), t.Bytes, t.DocFreq, t.TotalTermFreq))
            .ToList();
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> terms of a field in index order, starting at the first
    /// term that is greater than or equal to <paramref name="startAt"/>.
    /// </summary>
    public IReadOnlyList<TermInfo> BrowseTerms(string field, string? startAt, int count)
    {
        var result = new List<TermInfo>();
        var terms = MultiFields.GetTerms(_reader, field);
        if (terms is null || count <= 0)
            return result;

        var termsEnum = terms.GetEnumerator();
        bool positioned;
        if (string.IsNullOrEmpty(startAt))
            positioned = termsEnum.MoveNext();
        else
            positioned = termsEnum.SeekCeil(new BytesRef(startAt)) != TermsEnum.SeekStatus.END;

        while (positioned && result.Count < count)
        {
            var bytes = CopyBytes(termsEnum.Term);
            result.Add(new TermInfo(field, TermText.ToDisplay(bytes), bytes, termsEnum.DocFreq, termsEnum.TotalTermFreq));
            positioned = termsEnum.MoveNext();
        }
        return result;
    }

    public bool IsDeleted(int docId) => _liveDocs is not null && !_liveDocs.Get(docId);

    /// <summary>Returns the stored fields of a document.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is not between 0 and <see cref="MaxDoc"/> - 1.</exception>
    public DocumentView GetDocument(int docId)
    {
        if (docId < 0 || docId >= _reader.MaxDoc)
            throw new ArgumentOutOfRangeException(nameof(docId), docId, $"Document id must be between 0 and {_reader.MaxDoc - 1}.");

        if (IsDeleted(docId))
            return new DocumentView(docId, true, []);

        var document = _reader.Document(docId);
        var fields = new List<StoredFieldValue>();
        foreach (var field in document.Fields)
        {
            var (value, kind) = DescribeValue(field);
            fields.Add(new StoredFieldValue(field.Name, value, kind, FieldFlags.Describe(_fieldInfos.FieldInfo(field.Name), stored: true)));
        }
        return new DocumentView(docId, false, fields);
    }

    /// <summary>Parses a query with the classic query parser.</summary>
    /// <exception cref="ParseException">The query is invalid.</exception>
    public static Query ParseQuery(string query, string defaultField, string analyzerName = "Standard")
    {
        using var analyzer = CreateAnalyzer(analyzerName);
        var parser = new QueryParser(MatchVersion, defaultField, analyzer)
        {
            AllowLeadingWildcard = true,
        };
        return parser.Parse(query);
    }

    /// <summary>Creates a query matching exactly the given term.</summary>
    public static Query CreateTermQuery(TermInfo term) => new TermQuery(new Term(term.Field, new BytesRef(term.Bytes)));

    /// <summary>Parses the query with the classic query parser and runs it.</summary>
    /// <exception cref="ParseException">The query is invalid.</exception>
    public SearchResult Search(string query, string defaultField, string analyzerName = "Standard", int maxHits = 100) =>
        Search(ParseQuery(query, defaultField, analyzerName), maxHits);

    public SearchResult Search(Query query, int maxHits = 100)
    {
        var stopwatch = Stopwatch.StartNew();
        var topDocs = _searcher.Search(query, Math.Max(1, maxHits));
        var hits = topDocs.ScoreDocs
            .Select(sd => new SearchHit(sd.Doc, sd.Score, Summarize(sd.Doc)))
            .ToList();
        stopwatch.Stop();
        return new SearchResult(query, query.ToString(), topDocs.TotalHits, hits, stopwatch.Elapsed);
    }

    /// <summary>Explains how the score of a document was computed for a query.</summary>
    public string Explain(Query query, int docId) => _searcher.Explain(query, docId).ToString();

    public static Analyzer CreateAnalyzer(string name) => name switch
    {
        "Standard" => new StandardAnalyzer(MatchVersion),
        "Whitespace" => new WhitespaceAnalyzer(MatchVersion),
        "Simple" => new SimpleAnalyzer(MatchVersion),
        "Keyword" => new KeywordAnalyzer(),
        _ => throw new ArgumentException($"Unknown analyzer '{name}'.", nameof(name)),
    };

    public void Dispose()
    {
        _reader.Dispose();
        if (_ownsDirectory)
            _directory.Dispose();
    }

    private string Summarize(int docId, int maxLength = 200)
    {
        var parts = _reader.Document(docId).Fields
            .Select(f => $"{f.Name}: {DescribeValue(f).Value}");
        var summary = string.Join(" | ", parts).ReplaceLineEndings(" ");
        return summary.Length <= maxLength ? summary : summary[..maxLength] + "…";
    }

    private static (string Value, string Kind) DescribeValue(IIndexableField field)
    {
        var binary = field.GetBinaryValue();
        if (binary is not null)
            return ($"0x{Convert.ToHexString(binary.Bytes, binary.Offset, binary.Length)} ({binary.Length} bytes)", "binary");

        if (field.NumericType != Lucene.Net.Documents.NumericFieldType.NONE)
            return (field.GetStringValue(System.Globalization.CultureInfo.InvariantCulture) ?? "", "numeric");

        return (field.GetStringValue() ?? "", "string");
    }

    private static long CountTerms(Terms terms)
    {
        var size = terms.Count;
        if (size >= 0)
            return size;

        // Not all codecs / composite readers know the number of terms up front.
        long count = 0;
        var termsEnum = terms.GetEnumerator();
        while (termsEnum.MoveNext())
            count++;
        return count;
    }

    private long FileLength(string file)
    {
        try
        {
            return _directory.FileLength(file);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static byte[] CopyBytes(BytesRef bytes) => bytes.Bytes.AsSpan(bytes.Offset, bytes.Length).ToArray();
}
