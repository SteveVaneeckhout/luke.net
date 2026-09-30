using Lucene.Net.QueryParsers.Classic;

namespace LukeNet.Core.Tests;

public sealed class IndexInspectorTests : IClassFixture<TestIndex>
{
    private readonly TestIndex _index;

    public IndexInspectorTests(TestIndex index) => _index = index;

    [Fact]
    public void Open_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => IndexInspector.Open(System.IO.Path.Combine(_index.Path, "nope")));
    }

    [Fact]
    public void Open_DirectoryWithoutIndex_Throws()
    {
        var empty = Directory.CreateTempSubdirectory("lukenet-empty-");
        try
        {
            Assert.Throws<InvalidDataException>(() => IndexInspector.Open(empty.FullName));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void Overview_ReportsCounts()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var overview = inspector.GetOverview();

        Assert.Equal(3, overview.NumDocs);
        Assert.Equal(4, overview.MaxDoc);
        Assert.Equal(1, overview.DeletedDocs);
        Assert.True(overview.SegmentCount >= 1);
        Assert.True(overview.SizeInBytes > 0);
        Assert.Contains(overview.SegmentsFile, overview.Files);
        Assert.Equal("tests", overview.UserData["source"]);
    }

    [Fact]
    public void Fields_DescribeIndexAndDocValues()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var fields = inspector.GetFields().ToDictionary(f => f.Name);

        Assert.Equal(["blob", "body", "count", "id", "rank", "title"], fields.Keys.Order());
        Assert.False(fields["blob"].Indexed);
        Assert.True(fields["body"].HasTermVectors);
        Assert.Equal("NUMERIC", fields["rank"].DocValues);
        Assert.Equal(4, fields["id"].TermCount);
        Assert.Equal(4, fields["id"].DocCount);
    }

    [Fact]
    public void Segments_AreListed()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var segments = inspector.GetSegments();

        Assert.NotEmpty(segments);
        Assert.Equal(4, segments.Sum(s => s.DocCount));
        Assert.Equal(1, segments.Sum(s => s.DeletedDocs));
        Assert.All(segments, s => Assert.False(string.IsNullOrEmpty(s.Codec)));
        Assert.All(segments, s => Assert.True(s.SizeInBytes > 0));
    }

    [Fact]
    public void TopTerms_OrderedByDocFreq()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var top = inspector.GetTopTerms("title", 2);

        Assert.Equal(2, top.Count);
        Assert.Equal("quick", top[0].Text);
        Assert.Equal(2, top[0].DocFreq);
        Assert.True(top[0].DocFreq >= top[1].DocFreq);
    }

    [Fact]
    public void TopTerms_AllFields()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var top = inspector.GetTopTerms(null, 3);

        Assert.Equal(3, top.Count);
        Assert.All(top, t => Assert.Equal(4, t.DocFreq)); // "body", "text" in body and trie terms for count
    }

    [Fact]
    public void BrowseTerms_StartsAtGivenTerm()
    {
        using var inspector = IndexInspector.Open(_index.Path);

        Assert.Equal(["doc0", "doc1", "doc2", "doc3"], inspector.BrowseTerms("id", null, 10).Select(t => t.Text));
        Assert.Equal(["doc1", "doc2"], inspector.BrowseTerms("id", "doc1", 2).Select(t => t.Text));
        Assert.Equal(["doc3"], inspector.BrowseTerms("id", "doc25", 10).Select(t => t.Text));
        Assert.Empty(inspector.BrowseTerms("id", "zzz", 10));
        Assert.Empty(inspector.BrowseTerms("unknown", null, 10));
    }

    [Fact]
    public void NumericTrieTerms_AreShownAsHex()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var terms = inspector.BrowseTerms("count", null, 100);

        Assert.NotEmpty(terms);
        Assert.All(terms, t => Assert.StartsWith("0x", t.Text));
    }

    [Fact]
    public void Document_ShowsStoredFields()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var doc = inspector.GetDocument(1);

        Assert.False(doc.IsDeleted);
        var fields = doc.Fields.ToDictionary(f => f.Name);
        Assert.Equal("doc1", fields["id"].Value);
        Assert.Equal("Lazy dogs sleep", fields["title"].Value);
        Assert.Equal("10", fields["count"].Value);
        Assert.Equal("numeric", fields["count"].Kind);
        Assert.Equal("binary", fields["blob"].Kind);
        Assert.StartsWith("0xCAFE01", fields["blob"].Value);
        Assert.Equal("IFP--N--S", fields["title"].Flags);
        Assert.False(fields.ContainsKey("body"));
    }

    [Fact]
    public void Document_Deleted()
    {
        using var inspector = IndexInspector.Open(_index.Path);

        Assert.True(inspector.IsDeleted(3));
        Assert.True(inspector.GetDocument(3).IsDeleted);
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.GetDocument(4));
    }

    [Fact]
    public void Search_ParsesAndFinds()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var result = inspector.Search("quick", "title");

        Assert.Equal("title:quick", result.ParsedQuery);
        Assert.Equal(2, result.TotalHits);
        Assert.Equal([0, 2], result.Hits.Select(h => h.DocId).Order());
        Assert.Contains("id: doc0", result.Hits.Single(h => h.DocId == 0).Summary);
    }

    [Fact]
    public void Search_ExcludesDeletedDocuments()
    {
        using var inspector = IndexInspector.Open(_index.Path);

        Assert.Equal(3, inspector.Search("*:*", "title").TotalHits);
        Assert.Equal(0, inspector.Search("id:doc3", "title", "Keyword").TotalHits);
    }

    [Fact]
    public void Search_InvalidQuery_Throws()
    {
        using var inspector = IndexInspector.Open(_index.Path);

        Assert.Throws<ParseException>(() => inspector.Search("title:(quick", "title"));
    }

    [Fact]
    public void SearchTerm_UsesExactBytes()
    {
        using var inspector = IndexInspector.Open(_index.Path);
        var term = inspector.BrowseTerms("count", null, 1)[0];

        Assert.Equal(1, inspector.Search(IndexInspector.CreateTermQuery(term)).TotalHits);
    }

    [Fact]
    public void Explain_ReturnsText()
    {
        using var inspector = IndexInspector.Open(_index.Path);

        Assert.Contains("weight(title:quick", inspector.Explain(IndexInspector.ParseQuery("quick", "title"), 0));
    }
}
