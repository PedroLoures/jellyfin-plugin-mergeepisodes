# Jellyfin Plugin: Merge Episodes

A Jellyfin plugin that automatically detects and merges duplicate episodes (different quality versions of the same episode) into single entries with multiple playback versions.

## Features

### 🔀 Episode Merging
Automatically detects episodes that are different versions of the same content (e.g., 720p and 1080p copies) and merges them into a single library entry with multiple selectable versions.

**How it works:**
- Extracts a "base identity" from each episode's filename using the `SxxExx` pattern
- Groups episodes with the same identity (e.g., `Show S01E01 - 720p.mkv` and `Show S01E01 - 1080p.mkv`)
- Merges groups into one primary entry with linked alternate versions

### ✂️ Episode Splitting
Reverses the merge operation — splits previously-merged episodes back into individual entries.

- **Split Episodes** — splits only episodes in the current library view
- **Split All Episodes** — splits all merged episodes across all libraries

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

### ⚡ Cancellation Support
Long-running operations can be cancelled via the plugin UI or API. Cancellation is safe — it completes the current episode group before stopping.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Jellyfin Server (DI)                      │
├─────────────────────────────────────────────────────────────┤
│  PluginServiceRegistrator                                   │
│    ├── ConfigurationService (singleton)                     │
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
- **Split Everything** — Split all merged episodes across all libraries
- **Stop** — Cancel a running operation

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/MergeEpisodes/Merge` | Start merge operation |
| POST | `/MergeEpisodes/Split` | Start split operation |
| POST | `/MergeEpisodes/SplitAll` | Split all merged episodes |
| DELETE | `/MergeEpisodes/Cancel` | Cancel running operation |

## Testing

The plugin has a comprehensive test suite covering all features:

```bash
dotnet test
```

### Test Coverage

| Test Class | Tests | Coverage Area |
|------------|-------|---------------|
| `MergeEpisodesManagerTests` | 22 | Core merge/split, cancellation, corruption prevention, edge cases |
| `EpisodeIdentityTests` | 6 | Regex identity extraction (standard, multi-ep, case, no-match) |
| `ConfigurationServiceTests` | 6 | Null-safe config access, live value reflection |
| `MergeEpisodesTaskTests` | 7 | Scheduled task flag checking, progress, metadata |

### Key Test Scenarios
- Episodes with no duplicates → no merge
- Episodes without `SxxExx` pattern → skipped
- Cancellation between groups (safe)
- Concurrent operations (semaphore guard)
- Already-merged episodes (idempotent)
- Excluded library locations (skipped)
- Items deleted mid-operation (graceful handling)
- Auto-merge flag disabled → task skips entirely
- Auto-merge flag enabled → task calls merge service
- Config changes reflected in real-time (no caching)

## Identity Pattern

The regex extracts everything up to and including the `SxxExx` identifier:

```
^(.+?S\d+E\d+(?:(?:E|-E|n)\d+)*)
```

**Examples:**
| Filename | Identity |
|----------|----------|
| `Show S01E01 - 720p.mkv` | `Show S01E01` |
| `Show S01E01 - 1080p.mkv` | `Show S01E01` |
| `Show S01E01E02 - 720p.mkv` | `Show S01E01E02` |
| `Show S01E01-E02 Pilot.mkv` | `Show S01E01-E02` |
| `random_video.mkv` | `null` (skipped) |

## Requirements

- Jellyfin Server 10.11+
- .NET 9.0

## License

See repository for license information.
