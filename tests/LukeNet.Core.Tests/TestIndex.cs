using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace LukeNet.Core.Tests;

/// <summary>Creates a small index on disk that is deleted again when disposed.</summary>
public sealed class TestIndex : IDisposable
{
    public TestIndex()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lukenet-tests-" + Guid.NewGuid().ToString("N"));
        using var directory = FSDirectory.Open(Path);
        using var analyzer = new StandardAnalyzer(IndexInspector.MatchVersion);
        using var writer = new IndexWriter(directory, new IndexWriterConfig(IndexInspector.MatchVersion, analyzer));

        var bodyType = new FieldType(TextField.TYPE_NOT_STORED) { StoreTermVectors = true };
        string[] titles = ["The quick brown fox", "Lazy dogs sleep", "Quick thinking foxes", "Deleted document"];
        for (var i = 0; i < titles.Length; i++)
        {
            writer.AddDocument(new Document
            {
                new StringField("id", $"doc{i}", Field.Store.YES),
                new TextField("title", titles[i], Field.Store.YES),
                new Field("body", $"{titles[i]} body text", bodyType),
                new Int32Field("count", i * 10, Field.Store.YES),
                new StoredField("blob", new byte[] { 0xCA, 0xFE, (byte)i }),
                new NumericDocValuesField("rank", i),
            });
        }
        writer.Commit();
        writer.DeleteDocuments(new Term("id", "doc3"));
        writer.SetCommitData(new Dictionary<string, string> { ["source"] = "tests" });
        writer.Commit();
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
