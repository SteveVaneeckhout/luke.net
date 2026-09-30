using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LukeNet.Core;
using Lucene.Net.Search;

namespace LukeNet.App;

public partial class MainWindow : Window
{
    private const string AllFields = "(all fields)";

    private IndexInspector? _inspector;
    private Query? _lastQuery;
    private bool _busy;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        AnalyzerBox.ItemsSource = IndexInspector.AnalyzerNames;
        AnalyzerBox.SelectedIndex = 0;
        FlagsLegendText.Text = "Flags: " + FieldFlags.Legend;

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            PathBox.Text = initialPath;
            Opened += async (_, _) => await OpenIndexAsync(initialPath);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _inspector?.Dispose();
        base.OnClosed(e);
    }

    // ---------------------------------------------------------------- opening

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a Lucene index directory",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
            return;

        PathBox.Text = path;
        await OpenIndexAsync(path);
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await OpenIndexAsync(PathBox.Text);

    private async void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await OpenIndexAsync(PathBox.Text);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseIndex();
        SetStatus("Index closed.");
    }

    private async Task OpenIndexAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Enter or browse to an index directory first.", isError: true);
            return;
        }

        var loaded = await RunAsync($"Opening {path}…", () =>
        {
            var inspector = IndexInspector.Open(path.Trim());
            try
            {
                return new LoadedIndex(inspector, inspector.GetOverview(), inspector.GetFields(), inspector.GetSegments());
            }
            catch
            {
                inspector.Dispose();
                throw;
            }
        });
        if (loaded is null)
            return;

        CloseIndex();
        var (newInspector, overview, fields, segments) = loaded;
        _inspector = newInspector;
        ShowOverview(overview, fields, segments);

        var indexed = newInspector.GetIndexedFieldNames();
        TermFieldBox.ItemsSource = new[] { AllFields }.Concat(indexed).ToList();
        TermFieldBox.SelectedIndex = indexed.Count > 0 ? 1 : 0;
        DefaultFieldBox.ItemsSource = indexed;
        DefaultFieldBox.SelectedIndex = indexed.Count > 0 ? 0 : -1;

        DocIdBox.Maximum = Math.Max(0, overview.MaxDoc - 1);
        DocIdBox.Value = 0;
        ShowDocument(0);

        Tabs.IsEnabled = true;
        CloseButton.IsEnabled = true;
        Title = $"Luke.NET - {overview.Path}";
        SetStatus($"Opened {overview.Path}: {overview.NumDocs:N0} documents, {fields.Count} fields, {overview.SegmentCount} segments.");
    }

    private void CloseIndex()
    {
        _inspector?.Dispose();
        _inspector = null;
        _lastQuery = null;
        Tabs.IsEnabled = false;
        CloseButton.IsEnabled = false;
        Title = "Luke.NET - Lucene index viewer";
        OverviewItems.ItemsSource = null;
        FilesText.Text = null;
        FieldsGrid.ItemsSource = null;
        SegmentsGrid.ItemsSource = null;
        DocumentGrid.ItemsSource = null;
        DocInfoText.Text = null;
        FieldValueBox.Text = null;
        TermFieldBox.ItemsSource = null;
        TermsGrid.ItemsSource = null;
        DefaultFieldBox.ItemsSource = null;
        HitsGrid.ItemsSource = null;
        SearchInfoText.Text = null;
        ExplainBox.Text = null;
    }

    private void ShowOverview(IndexOverview overview, IReadOnlyList<FieldSummary> fields, IReadOnlyList<SegmentSummary> segments)
    {
        var items = new List<KeyValuePair<string, string>>
        {
            new("Path", overview.Path),
            new("Documents", overview.NumDocs.ToString("N0")),
            new("Max doc", overview.MaxDoc.ToString("N0")),
            new("Deleted docs", overview.DeletedDocs.ToString("N0")),
            new("Fields", fields.Count.ToString("N0")),
            new("Segments", overview.SegmentCount.ToString("N0")),
            new("Size", ByteSizeConverter.Format(overview.SizeInBytes)),
            new("Generation", overview.Generation.ToString(CultureInfo.InvariantCulture)),
            new("Version", overview.Version.ToString(CultureInfo.InvariantCulture)),
            new("Segments file", overview.SegmentsFile),
            new("Lucene versions", string.Join(", ", segments.Select(s => s.LuceneVersion).Distinct())),
            new("Codecs", string.Join(", ", segments.Select(s => s.Codec).Distinct())),
        };
        items.AddRange(overview.UserData.Select(kv => new KeyValuePair<string, string>($"User data: {kv.Key}", kv.Value)));

        OverviewItems.ItemsSource = items;
        FilesText.Text = string.Join(Environment.NewLine, overview.Files);
        FieldsGrid.ItemsSource = fields;
        SegmentsGrid.ItemsSource = segments;
    }

    // -------------------------------------------------------------- documents

    private void OnDocIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue is { } value)
            ShowDocument((int)value);
    }

    private void OnFirstDocClick(object? sender, RoutedEventArgs e) => GoToDocument(0);

    private void OnPrevDocClick(object? sender, RoutedEventArgs e) => GoToDocument((int)(DocIdBox.Value ?? 0) - 1);

    private void OnNextDocClick(object? sender, RoutedEventArgs e) => GoToDocument((int)(DocIdBox.Value ?? 0) + 1);

    private void OnLastDocClick(object? sender, RoutedEventArgs e) => GoToDocument((int)DocIdBox.Maximum);

    private void GoToDocument(int docId)
    {
        if (_inspector is null)
            return;
        DocIdBox.Value = Math.Clamp(docId, 0, Math.Max(0, _inspector.MaxDoc - 1));
    }

    private void ShowDocument(int docId)
    {
        FieldValueBox.Text = null;
        if (_inspector is null || _inspector.MaxDoc == 0)
        {
            DocumentGrid.ItemsSource = null;
            DocInfoText.Text = "The index contains no documents.";
            return;
        }

        try
        {
            var document = _inspector.GetDocument(docId);
            DocumentGrid.ItemsSource = document.Fields;
            DocInfoText.Text = document.IsDeleted
                ? $"of {_inspector.MaxDoc - 1:N0} — this document is deleted"
                : $"of {_inspector.MaxDoc - 1:N0} — {document.Fields.Count} stored field(s)";
        }
        catch (Exception ex)
        {
            DocumentGrid.ItemsSource = null;
            DocInfoText.Text = ex.Message;
        }
    }

    private void OnDocumentFieldSelected(object? sender, SelectionChangedEventArgs e) =>
        FieldValueBox.Text = (DocumentGrid.SelectedItem as StoredFieldValue)?.Value;

    // ------------------------------------------------------------------ terms

    private async void OnTopTermsClick(object? sender, RoutedEventArgs e)
    {
        if (_inspector is not { } inspector)
            return;
        var field = SelectedTermField();
        var count = (int)(TermCountBox.Value ?? 50);

        var terms = await RunAsync("Collecting top terms…", () => inspector.GetTopTerms(field, count));
        if (terms is null)
            return;
        TermsGrid.ItemsSource = terms;
        SetStatus($"Top {terms.Count:N0} terms of {field ?? "all fields"}.");
    }

    private async void OnBrowseTermsClick(object? sender, RoutedEventArgs e) => await BrowseTermsAsync();

    private async void OnTermStartKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await BrowseTermsAsync();
    }

    private async Task BrowseTermsAsync()
    {
        if (_inspector is not { } inspector)
            return;
        if (SelectedTermField() is not { } field)
        {
            SetStatus("Select a single field to browse its terms.", isError: true);
            return;
        }
        var start = TermStartBox.Text;
        var count = (int)(TermCountBox.Value ?? 50);

        var terms = await RunAsync("Reading terms…", () => inspector.BrowseTerms(field, start, count));
        if (terms is null)
            return;
        TermsGrid.ItemsSource = terms;
        SetStatus(terms.Count == 0
            ? $"No terms in {field} at or after '{start}'."
            : $"{terms.Count:N0} terms of {field} starting at '{terms[0].Text}'.");
    }

    private string? SelectedTermField() =>
        TermFieldBox.SelectedItem is string field && field != AllFields ? field : null;

    private async void OnTermDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TermsGrid.SelectedItem is not TermInfo term)
            return;
        QueryBox.Text = $"{term.Field}:{term.Text}";
        Tabs.SelectedIndex = 3;
        await RunSearchAsync(IndexInspector.CreateTermQuery(term));
    }

    // ----------------------------------------------------------------- search

    private async void OnSearchClick(object? sender, RoutedEventArgs e) => await ParseAndSearchAsync();

    private async void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await ParseAndSearchAsync();
    }

    private async Task ParseAndSearchAsync()
    {
        var text = QueryBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Enter a query first (use *:* to match all documents).", isError: true);
            return;
        }
        if (DefaultFieldBox.SelectedItem is not string field)
        {
            SetStatus("The index has no indexed fields to search.", isError: true);
            return;
        }

        Query query;
        try
        {
            query = IndexInspector.ParseQuery(text, field, AnalyzerBox.SelectedItem as string ?? "Standard");
        }
        catch (Exception ex)
        {
            _lastQuery = null;
            SearchInfoText.Text = null;
            HitsGrid.ItemsSource = null;
            ExplainBox.Text = null;
            SetStatus($"Invalid query: {FirstLine(ex.Message)}", isError: true);
            return;
        }
        await RunSearchAsync(query);
    }

    private async Task RunSearchAsync(Query query)
    {
        if (_inspector is not { } inspector)
            return;
        var maxHits = (int)(MaxHitsBox.Value ?? 100);

        var result = await RunAsync("Searching…", () => inspector.Search(query, maxHits));
        if (result is null)
            return;

        _lastQuery = result.Query;
        HitsGrid.ItemsSource = result.Hits;
        ExplainBox.Text = null;
        SearchInfoText.Text = $"Parsed query: {result.ParsedQuery}\n" +
                              $"{result.TotalHits:N0} hit(s), showing {result.Hits.Count:N0} — {result.Elapsed.TotalMilliseconds:0.#} ms";
        SetStatus($"{result.TotalHits:N0} hit(s) for {result.ParsedQuery}.");
    }

    private void OnHitSelected(object? sender, SelectionChangedEventArgs e) =>
        ExplainButton.IsEnabled = HitsGrid.SelectedItem is SearchHit && _lastQuery is not null;

    private void OnHitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (HitsGrid.SelectedItem is not SearchHit hit)
            return;
        Tabs.SelectedIndex = 1;
        GoToDocument(hit.DocId);
    }

    private async void OnExplainClick(object? sender, RoutedEventArgs e)
    {
        if (_inspector is not { } inspector || _lastQuery is not { } query || HitsGrid.SelectedItem is not SearchHit hit)
            return;
        var explanation = await RunAsync("Explaining…", () => inspector.Explain(query, hit.DocId));
        if (explanation is null)
            return;
        ExplainBox.Text = explanation;
        SetStatus($"Explanation of document {hit.DocId}.");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Runs work off the UI thread, reporting progress and errors in the status bar.</summary>
    private async Task<T?> RunAsync<T>(string message, Func<T> work) where T : class
    {
        if (_busy)
        {
            SetStatus("Still busy, please wait…");
            return null;
        }

        _busy = true;
        Cursor = new Cursor(StandardCursorType.Wait);
        SetStatus(message);
        try
        {
            return await Task.Run(work);
        }
        catch (Exception ex)
        {
            SetStatus(FirstLine(ex.Message), isError: true);
            return null;
        }
        finally
        {
            _busy = false;
            Cursor = Cursor.Default;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        if (isError)
            StatusText.Foreground = Brushes.OrangeRed;
        else
            StatusText.ClearValue(TextBlock.ForegroundProperty);
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private sealed record LoadedIndex(
        IndexInspector Inspector,
        IndexOverview Overview,
        IReadOnlyList<FieldSummary> Fields,
        IReadOnlyList<SegmentSummary> Segments);
}
