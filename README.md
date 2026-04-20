<p align="center">
<h1 align="center">Merge Episodes Plugin</h1>
<p align="center">
<em>Part of the <a href="https://jellyfin.org">Jellyfin</a> Project</em>
</p>
</p>

A Jellyfin plugin that automatically groups duplicate episodes (different quality/format versions of the same episode) into single library entries with multiple selectable playback versions.

> **Since Jellyfin already handles movie version merging natively, this plugin focuses exclusively on TV episodes.** It follows the [Jellyfin TV show naming conventions](https://jellyfin.org/docs/general/server/media/shows/) — specifically the `Show Name SxxEyy` pattern.

This is a simplified, rewritten version of [Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions) by danieladov.

---

## 📖 Table of Contents

- [How It Works](#-how-it-works)
- [Installation](#-installation)
- [Configuration](#-configuration)
- [Supported Naming Conventions](#-supported-naming-conventions)
- [API Reference](#-api-reference)
- [Architecture](#-architecture)
- [Database Safety](#-database-safety)
- [Testing](#-testing)
- [Known Limitations](#-known-limitations)

---

## 🧠 How It Works

The plugin extracts a **base identity** from each episode's filename by matching everything up to (and including) the `SxxExx` pattern. Everything **after** the first space past `SxxExx` is ignored — this is how different quality versions are recognized as the same episode.

### Identity Matching Examples

| File A | File B | Same Episode? |
|--------|--------|:-------------:|
| `Show Name S01E01 - 720p.mkv` | `Show Name S01E01 - 1080p.mkv` | ✅ Yes |
| `Show Name S00E01 Test - 1080p.mkv` | `Show Name S00E01 - 720p.mkv` | ✅ Yes |
| `Show Name S01E01 Test - 1080p.mkv` | `Show Name S01E**02** Test - 1080p.mkv` | ❌ No |
| `Show Name S01E01 Test - 1080p.mkv` | `Show Name S**02**E01 Test - 1080p.mkv` | ❌ No |

> **In short:** Anything with the same text up to and including the `SxxExx` identifier is considered the same episode, regardless of quality tags, codec info, or other suffixes.

### What Happens When You Merge

1. The plugin scans your library for all eligible episodes
2. Groups episodes that share the same base identity
3. Picks the first episode as the **primary** version
4. Links all other versions as **alternate versions** under the primary
5. In Jellyfin's UI, these appear as a single episode with a "Version" selector

### What Happens When You Split

- **Split Episodes** — Reverses the merge: unlinks alternate versions from primary episodes only (the normal undo)
- **Split All Episodes (Deep Clean)** — Splits ALL episodes with any merge state — including orphaned secondaries. Use this to fix issues left by older plugin versions or corrupted states

---

## 📦 Installation

### From Repository (Recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**
2. Click **Add** and paste this URL:
   ```
   https://raw.githubusercontent.com/PedroLoures/JellyfinPluginManifest/main/manifest.json
   ```
3. Go to **Catalog** and search for **Merge Episodes**
4. Click on it and click **Install**
5. **Restart Jellyfin**

### Manual Installation

1. Download the latest release from the [Releases page](https://github.com/PedroLoures/jellyfin-plugin-mergeepisodes/releases)
2. Extract the DLL into your Jellyfin plugins directory (e.g., `/config/plugins/MergeEpisodes/`)
3. **Restart Jellyfin**

---

## ⚙️ Configuration

Access the plugin settings in **Jellyfin Dashboard → Plugins → Merge Episodes**.

### Included Library Paths

After installation, **no paths are included by default** — the plugin won't process anything until you explicitly select which libraries to include. This is a safety measure to prevent unintended merges.

- Check the paths you want the plugin to process
- Use **Select All** / **Deselect All** for quick toggling
- Click **Save Configuration** to apply

### Manual Operations

| Button | Description |
|--------|-------------|
| 🔗 **Merge All Episodes** | Scan included libraries and merge duplicate episodes |
| ✂️ **Split All Episodes** | Split merged episodes back into individual entries |
| ⚠️ **Split Everything — Fix Old Plugin Issues** | Deep clean: split ALL episodes with any merge state |
| ⏹️ **Stop** | Cancel a running operation (finishes the current group safely) |

### Activity Log

The config page includes a real-time activity log that shows:
- Operation start/completion with elapsed time
- Success/failure counts per operation
- Individual failed items (if any)

### Scheduled Task

The plugin registers a **"Merge All Episodes"** task in Jellyfin's **Scheduled Tasks** UI. It has **no automatic triggers** by default — you can add custom triggers (e.g., daily, after library scan) through Jellyfin's built-in task scheduling.

---

## 📂 Supported Naming Conventions

The plugin follows [Jellyfin's official TV show naming documentation](https://jellyfin.org/docs/general/server/media/shows/).

### Standard Format

```
Series Name (Year) SxxExx Episode Title - Tag.extension
```

```
Show Name S01E01.mkv
Show Name S01E01 - 720p.mkv
Show Name S01E01 - 1080p - HEVC.mkv
Series Name A (2010) S01E03.mkv
Series Name A (2021) S01E01 Title.avi
Awesome TV Show (2024) S01E01 episode name.mp4
Show S10E100 Nome Do Episódio - BluRay.mkv
```

### Multi-Episode Format

```
Series Name SxxExx-Eyy.ext       (dash separator)
Series Name SxxExxEyy.ext        (concatenated)
Series Name SxxExxnyy.ext        (n separator)
```

```
Series Name A (2010) S01E01-E02.mkv
Series Name B (2018) S02E01-E02.mkv
Show S01E01E02 Pilot Part 1 and 2 - 1080p.mkv
Show S01E01n02 Nome de Epi - 720p.mkv
```

### 3D and Quality Variants

These all merge together because they share the same identity:

```
Series Name A (2022) S01E01 Some Episode.3d.ftab.mp4
Series Name A (2022) S01E01 Some Episode.3d.hsbs.mp4
Series Name A (2022) S01E01 Some Episode.mkv
```

### Metadata Provider IDs

```
Jellyfin Documentary (2030) [imdbid-tt00000000] S01E01.mkv
```

### Specials (Season 00)

```
Series Name A S00E01.mkv
Series Name A S00E02.mkv
```

---

## 🔌 API Reference

All endpoints require admin authorization (`RequiresElevation` policy).

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/MergeEpisodes/MergeEpisodes` | Start merge operation |
| `POST` | `/MergeEpisodes/SplitEpisodes` | Split merged episodes (primary only) |
| `POST` | `/MergeEpisodes/SplitAllEpisodes` | Deep clean: split all episodes with any merge state |
| `POST` | `/MergeEpisodes/Cancel` | Cancel the currently running operation |

### Response Format

Merge and split endpoints return an `OperationResult`:

```json
{
  "Succeeded": 12,
  "Failed": 1,
  "FailedItems": ["Show Name S01E05"]
}
```

If cancelled:

```json
{
  "message": "Operation cancelled",
  "cancelled": true
}
```

---

## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     Jellyfin Server (DI)                     │
├──────────────────────────────────────────────────────────────┤
│  PluginServiceRegistrator                                    │
│    ├── ConfigurationService          (singleton)             │
│    ├── LibraryQueryService           (singleton)             │
│    └── IEpisodeMergeService →                                │
│           MergeEpisodesManager       (singleton)             │
├──────────────────────────────────────────────────────────────┤
│  MergeEpisodesController  (REST API, RequiresElevation)      │
│    └── IEpisodeMergeService via DI                           │
├──────────────────────────────────────────────────────────────┤
│  MergeEpisodesTask  (IScheduledTask, no auto triggers)       │
│    └── IEpisodeMergeService.MergeEpisodesAsync               │
└──────────────────────────────────────────────────────────────┘
```

### Key Components

| File | Purpose |
|------|---------|
| `MergeEpisodesManager.cs` | Core engine — merge/split logic with DB corruption prevention |
| `IEpisodeMergeService.cs` | Interface for DI decoupling |
| `LibraryQueryService.cs` | Library querying with include-path filtering |
| `ConfigurationService.cs` | Null-safe centralized config access |
| `MergeEpisodesTask.cs` | Jellyfin scheduled task (manual triggers only) |
| `MergeEpisodesController.cs` | REST API (4 endpoints) |
| `PluginServiceRegistrator.cs` | DI registration |
| `PluginConfiguration.cs` | Config model (`LocationsIncluded`) |
| `configPage.html` | Plugin settings UI with activity log |
| `OperationResult.cs` | Result record type |

---

## 🛡️ Database Safety

The plugin implements multiple safety mechanisms to prevent Jellyfin database corruption:

| Mechanism | Description |
|-----------|-------------|
| **SemaphoreSlim Guard** | Only one merge/split operation at a time; concurrent requests wait for the current atomic unit to complete |
| **Primary-First Writes** | The "master" episode is always saved before children, so children always reference a valid parent |
| **CancellationToken.None for Writes** | DB writes are never abandoned mid-transaction, even during cancellation |
| **Between-Group Cancellation** | Cancellation is checked between episode groups, never mid-group |
| **Per-Item Resilience** | If one child fails (deleted mid-op, I/O error), remaining children are still processed |
| **Orphaned Secondary Cleanup** | When splitting, orphaned secondary items (stale `PrimaryVersionId`) are cleaned up |
| **Null Path Guards** | Corrupted DB entries with null paths are safely skipped |
| **HashSet Deduplication** | Duplicate paths (e.g., symlinks) produce only one `LinkedChild` entry |
| **Dispose Safety** | `volatile _disposed` flag + `ObjectDisposedException.ThrowIf` prevents use-after-dispose corruption |

---

## 🧪 Testing

```bash
dotnet test
```

### Test Coverage — 86 Tests

| Test Class | Tests | Coverage |
|------------|:-----:|----------|
| `MergeEpisodesManagerTests` | 38 | Core merge/split, cancellation, concurrent ops, corruption prevention, dispose safety, edge cases |
| `EpisodeIdentityTests` | 34 | Regex identity — standard, multi-ep, 3D, specials, metadata IDs, case-insensitive, no-match |
| `LibraryQueryServiceTests` | 10 | Library querying, include-path filtering, null path safety |
| `MergeEpisodesTaskTests` | 5 | Scheduled task metadata, progress, cancellation token wiring |
| `ConfigurationServiceTests` | 3 | Null-safe access, default values, live value reflection |

### Key Test Scenarios

- **No duplicates / no episodes / no SxxExx pattern** → zero counts, no side effects
- **Already-merged episodes** → idempotent re-merge
- **Duplicate paths (symlinks)** → deduplicated via HashSet
- **Null paths / empty filenames** → safely excluded
- **Null LinkedChild.Path (corrupted data)** → skipped without crash
- **Item deleted between scan and merge** → graceful null handling
- **Child update failure** → remaining children still processed
- **Concurrent operations** → semaphore prevents overlap
- **Rapid cancellation** → no deadlocks, no crashes
- **Use after dispose** → `ObjectDisposedException` thrown
- **Multiple included paths** → episodes from both are eligible
- **Empty included paths** → nothing processed (safe default)

---

## ⚠️ Known Limitations

1. **Multi-Part Files** — Jellyfin's documentation states that multi-part files (`S01E01-part-1.mkv`, `S01E01-part-2.mkv`) do not work with merging. This plugin will incorrectly group them as the same episode because `-part-N` comes after `SxxExx`. **Do not use multi-part files with this plugin.**

2. **Movies Not Supported** — Jellyfin already handles movie version merging natively. This plugin only processes TV episodes.

3. **No Automatic Triggers** — The scheduled task has no default triggers. You must manually add triggers in Jellyfin's Scheduled Tasks UI or run operations from the plugin's config page.

---

## 🙏 Credits

- Me
- Original [Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions) plugin by [danieladov](https://github.com/danieladov)
- Built for the [Jellyfin](https://jellyfin.org) media system

## Identity Pattern

The regex extracts everything up to and including the `SxxExx` identifier:

```
^(.+?S\d+E\d+(?:(?:E|-E|n)\d+)*)
```

**Examples:**
| Filename | Identity |
|----------|----------|
| `Series Name A (2010) S01E03.mkv` | `Series Name A (2010) S01E03` |
| `Show S01E01 - 720p.mkv` | `Show S01E01` |
| `Show S01E01 - 1080p.mkv` | `Show S01E01` |
| `Show S01E01E02 - 720p.mkv` | `Show S01E01E02` |
| `Show S01E01-E02 Pilot.mkv` | `Show S01E01-E02` |
| `Show S01E01 The Beginning.3d.ftab.mp4` | `Show S01E01` |
| `Jellyfin Documentary (2030) [imdbid-tt00000000] S01E01.mkv` | `Jellyfin Documentary (2030) [imdbid-tt00000000] S01E01` |
| `random_video.mkv` | `null` (skipped) |

Episodes with the same identity (compared case-insensitively) are merged into a single entry.

## Requirements

- Jellyfin Server 10.11+
- .NET 9.0

## License

See repository for license information.
