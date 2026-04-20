# Jellyfin Plugin: Merge Episodes

A Jellyfin plugin that automatically detects and merges duplicate episodes (different quality versions of the same episode) into single entries with multiple playback versions.

## Features

### 🔀 Episode Merging
Automatically detects episodes that are different versions of the same content (e.g., 720p and 1080p copies) and merges them into a single library entry with multiple selectable versions.

**How it works:**
- Extracts a "base identity" from each episode's filename using the `SxxExx` pattern
- Groups episodes with the same identity (e.g., `Show S01E01 - 720p.mkv` and `Show S01E01 - 1080p.mkv`)
- Merges groups into one primary entry with linked alternate versions
- Selects the highest-resolution non-3D file as the primary version

### ✂️ Episode Splitting
Reverses the merge operation — splits previously-merged episodes back into individual entries.

- **Split Episodes** — splits only primary merged episodes (the normal undo)
- **Split All Episodes** — deep clean that splits ALL episodes with any merge state (primary or secondary), intended to fix issues left by older plugin versions

### ⏱️ Scheduled Auto-Merge
Optionally runs the merge operation automatically on a 24-hour interval.

- Controlled by the **"Automatically merge episodes after library scans"** checkbox in plugin settings
- Disabled by default (opt-in) — won't run unless explicitly enabled
- Respects the config flag at execution time (not cached at task creation)

### 🚫 Library Exclusions
Configure specific library locations to be excluded from merging. Episodes in excluded paths are skipped entirely.

### 🛡️ Database Corruption Prevention
Multiple safety mechanisms protect the Jellyfin database from corruption:

1. **SemaphoreSlim Guard** — Ensures only one merge/split operation runs at a time; concurrent requests wait for the current atomic unit to complete
2. **Primary-First Write Order** — The "master" episode is always written to the database before child episodes, so children always have a valid parent reference
3. **CancellationToken.None for DB Writes** — Database writes are never abandoned mid-transaction, even if cancellation is requested
4. **Cancellation Between Groups Only** — Cancellation is checked between episode groups, never in the middle of processing a single group
5. **Per-Item Resilience** — If one child episode fails to update (e.g., deleted mid-operation, disk I/O error), remaining children are still processed
6. **Null Path Guards** — Corrupted DB entries with null paths are safely skipped without crashing

### ⚡ Cancellation Support
Long-running operations can be cancelled via the plugin UI or API. Cancellation is safe — it completes the current episode group before stopping.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Jellyfin Server (DI)                      │
├─────────────────────────────────────────────────────────────┤
│  PluginServiceRegistrator                                   │
│    ├── ConfigurationService (singleton)                     │
│    ├── LibraryQueryService (singleton)                      │
│    └── IEpisodeMergeService → MergeEpisodesManager (single) │
├─────────────────────────────────────────────────────────────┤
│  MergeEpisodesController (REST API)                         │
│    └── Uses IEpisodeMergeService via DI                     │
├─────────────────────────────────────────────────────────────┤
│  MergeEpisodesTask (IScheduledTask)                         │
│    ├── Checks ConfigurationService.AutoMergeAfterLibraryScan│
│    └── Calls IEpisodeMergeService.MergeEpisodesAsync        │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

| File | Purpose |
|------|---------|
| `MergeEpisodesManager.cs` | Core engine — merge/split logic with corruption prevention |
| `IEpisodeMergeService.cs` | Interface for DI decoupling |
| `LibraryQueryService.cs` | Library querying with exclusion filtering |
| `ConfigurationService.cs` | Null-safe centralized config access |
| `MergeEpisodesTask.cs` | Scheduled task (24hr auto-merge) |
| `MergeEpisodesController.cs` | REST API endpoints |
| `PluginServiceRegistrator.cs` | DI registration |
| `PluginConfiguration.cs` | Config model (exclusions, auto-merge flag) |
| `configPage.html` | Plugin settings UI |
| `OperationResult.cs` | Result record type |

## Configuration

Access the plugin settings in **Jellyfin Dashboard → Plugins → Merge Episodes**.

### Options
- **Automatically merge episodes after library scans** — Enable to run merge automatically every 24 hours
- **Excluded Libraries** — Select library locations to exclude from merging

### Operations (Manual)
- **Merge** — Run merge operation now
- **Split** — Split merged episodes in current view
- **Split Everything** — Deep clean: split all merged episodes across all libraries
- **Stop** — Cancel a running operation

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/MergeEpisodes/MergeEpisodes` | Start merge operation |
| POST | `/MergeEpisodes/SplitEpisodes` | Start split operation |
| POST | `/MergeEpisodes/SplitAllEpisodes` | Deep clean: split all merged episodes |
| POST | `/MergeEpisodes/Cancel` | Cancel running operation |

All endpoints require admin authorization (`RequiresElevation` policy).

## Supported Naming Conventions

The plugin follows [Jellyfin's official TV show naming documentation](https://jellyfin.org/docs/general/server/media/shows/). Supported file naming patterns:

### Standard Format
```
Series Name (Year) SxxExx Episode Title - Tag.ext
```

**Examples:**
```
Series Name A (2010) S01E03.mkv
Series Name A (2021) S01E01 Title.avi
Awesome TV Show (2024) S01E01 episode name.mp4
My Show S02E15 - 1080p - HEVC.mkv
Show S10E100 Nome Do Episódio - BluRay.mkv
```

### Multi-Episode Format
```
Series Name (Year) SxxExx-Eyy.ext
Series Name (Year) SxxExxEyy.ext
Series Name (Year) SxxExxnyy.ext
```

**Examples:**
```
Series Name A (2010) S01E01-E02.mkv
Series Name B (2018) S02E01-E02.mkv
Show S01E01E02 Pilot Part 1 and 2 - 1080p.mkv
Show S01E01n02 Nome de Epi - 720p.mkv
```

### 3D and Quality Variants (Merge Targets)
```
Series Name A (2022) S01E01 Some Episode.3d.ftab.mp4
Series Name A (2022) S01E01 Some Episode.3d.hsbs.mp4
Series Name A (2022) S01E01 Some Episode.mkv         ← these all merge together
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

### ⚠️ Known Limitation: Multi-Part Files
Jellyfin's documentation states that multi-part files (`S01E01-part-1.mkv`, `S01E01-part-2.mkv`) **do not work with merging**. This plugin will incorrectly group them as the same episode because the `-part-N` suffix comes after the `SxxExx` identifier. **Do not use multi-part files with this plugin.**

## Testing

The plugin has a comprehensive test suite covering all features:

```bash
dotnet test
```

### Test Coverage (81 tests)

| Test Class | Tests | Coverage Area |
|------------|-------|---------------|
| `MergeEpisodesManagerTests` | 26 | Core merge/split, cancellation, corruption prevention, edge cases |
| `EpisodeIdentityTests` | 34 | Regex identity extraction — standard, multi-ep, Jellyfin doc format, 3D, specials, year+metadata ID, case-insensitive, no-match, multi-part |
| `ConfigurationServiceTests` | 6 | Null-safe config access, live value reflection |
| `MergeEpisodesTaskTests` | 7 | Scheduled task flag checking, progress, metadata |
| `LibraryQueryServiceTests` | 8 | Library querying, exclusion filtering, null path safety |

### Key Test Scenarios

**Merge:**
- Episodes with no duplicates → no merge
- Episodes without `SxxExx` pattern → skipped
- Jellyfin naming conventions (year, metadata ID, 3D tags, specials)
- Already-merged episodes → idempotent
- Duplicate paths (symlinks) → deduplicated
- Episode with null path → safely skipped

**Split:**
- Items deleted between scan and split → graceful handling
- Stale linked items → per-item try/catch, doesn't abort others

**DB Corruption Prevention:**
- Primary updated before children (write order)
- Child update throws → remaining children still processed
- Concurrent operations → semaphore guard
- Cancellation between groups (never mid-transaction)
- Null `LinkedChild.Path` (corrupted data) → skipped

**Configuration:**
- Auto-merge flag disabled → task skips entirely
- Auto-merge flag enabled → task calls merge service
- Config changes reflected in real-time (no caching)
- Null path in excluded library check → returns false (eligible)

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
