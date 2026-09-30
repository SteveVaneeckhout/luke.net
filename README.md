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

## Download

Ready-to-run builds for Windows, Linux and macOS are on the [Releases](../../releases) page. Each download is a
single executable that needs no .NET installation: unpack it and run `LukeNet` (`LukeNet.exe` on Windows),
optionally followed by the path of an index.

The builds are not code signed, so Windows SmartScreen may ask for confirmation. On macOS remove the quarantine
flag first: `xattr -d com.apple.quarantine LukeNet`.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/LukeNet.App                   # then use Browse… to choose an index directory
dotnet run --project src/LukeNet.App -- /path/to/index # open an index directly
dotnet test                                            # run the unit tests
```

To produce a single self-contained executable, pass a runtime identifier (`win-x64`, `win-arm64`, `linux-x64`,
`osx-x64` or `osx-arm64`):

```sh
dotnet publish src/LukeNet.App -c Release -r win-x64 -o publish
```

## Releases

The [Build](.github/workflows/build.yml) workflow builds and tests every push and pull request. It attaches the
executables for all platforms to the workflow run as downloadable artifacts.

To publish a release, push a version tag:

```sh
git tag v1.0.0
git push origin v1.0.0
```

The workflow then creates a GitHub release named after the tag, with generated release notes and a zip (Windows) or
tar.gz (Linux, macOS) per platform. Tags with a suffix, such as `v1.1.0-beta.1`, are marked as pre-releases.

## Project layout

| Project | Description |
| --- | --- |
| `src/LukeNet.Core` | UI independent `IndexInspector` that reads the index (overview, fields, segments, terms, documents, search). |
| `src/LukeNet.App` | Avalonia desktop application. |
| `tests/LukeNet.Core.Tests` | xUnit tests for the core library that run against a generated index. |
