# Luke.NET

A simple, cross-platform viewer for Lucene index files, inspired by [Luke](https://github.com/DmitryKey/luke).
Built with .NET 10, [Lucene.NET](https://lucenenet.apache.org/) 4.8 and [Avalonia](https://avaloniaui.net/).

It reads indexes written by Lucene / Lucene.NET 4.x. The index is opened read-only and never modified.

## Features

- **Overview**: document counts, deletions, size, generation, commit user data and commit files. It also lists
  the fields (index options, term and document counts, norms, term vectors, payloads, doc values) and the segments
  (size, codec, Lucene version).
- **Documents**: step through documents by id and see their stored fields, Luke style flags
  (`IFPOVNYDS`) and full values. Deleted documents are marked as deleted.
- **Terms**: top terms by document frequency for one field or all fields, or browse a field's terms in index
  order from any starting point. Binary and numeric (trie) terms are shown as hex. Double-click a term to find
  the documents that contain it.
- **Search**: classic Lucene query syntax with a choice of analyzer and default field, and score explanations.
  Double-click a hit to open the document.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/LukeNet.App                   # then use Browse… to choose an index directory
dotnet run --project src/LukeNet.App -- /path/to/index # open an index directly
dotnet test                                            # run the unit tests
```

To produce a standalone executable, for example for Windows:

```sh
dotnet publish src/LukeNet.App -c Release -r win-x64 --self-contained
```

## Project layout

| Project | Description |
| --- | --- |
| `src/LukeNet.Core` | UI independent `IndexInspector` that reads the index (overview, fields, segments, terms, documents, search). |
| `src/LukeNet.App` | Avalonia desktop application. |
| `tests/LukeNet.Core.Tests` | xUnit tests for the core library that run against a generated index. |
