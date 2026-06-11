# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] — 2026-06-11

### Added
- **Content-pack externalization.** Copyrighted IELTS material (Cambridge tests/audio/
  figures, Oxford 5000, PHaVE) is no longer embedded in the assemblies. A fresh clone
  now builds without it.
  - `IContentStore` / `FileSystemContentStore` — single source of truth for what content
    is present under `%AppData%\EnglishStudio\IeltsContent\`.
  - `ContentImportService` (new `EnglishStudio.Content` project) — imports a content pack
    from a folder or `.zip`, validates the manifest (fail-fast before any copy/seed; rejects a
    `packVersion` newer than supported or a section whose key file is missing), copies into
    `IeltsContent\`, and re-runs the idempotent seeders.
  - Content-import UI: themed importer window with byte progress, reachable from Settings
    (📦 Контент) and from each locked section.
  - `ContentMissingView` gating: Reading/Listening/Writing/Speaking/Mock hubs and the
    Oxford dictionary prompt to import when their content is absent.
  - `tools/ContentPackBuilder` — assembles a distributable pack + `manifest.json` from the
    on-disk `Seed/` assets.
  - Added a PHaVE source filter to the dictionary (`WordSource.Phave`).
  - IELTS band-descriptor rubrics used by AI grading were moved out of the assembly into the pack's
    `Rubrics/` section (loaded at runtime via `RubricLoader` from `IeltsContent\Rubrics\`); no
    copyrighted descriptor text remains in the published source.
  - Docs: [`docs/CONTENT_PACK.md`](docs/CONTENT_PACK.md).
- **UI localization (RU/EN).** All user-facing strings moved to `Strings.resx` /
  `Strings.en.resx` (736 keys, full parity) with live language switching from the title bar;
  module-emitted messages (progress, import errors, verdicts) flow through a new
  `IMessageLocalizer` so they follow the UI language too.
- **Crash safety net.** Global `DispatcherUnhandledException` / `UnobservedTaskException` /
  `AppDomain` handlers: errors are logged and reported to the user instead of killing the
  process; crash reports are written to `%AppData%\EnglishStudio\Crashes` and offered for
  review on the next start after a fatal crash.
- **Database backup.** A consistent SQLite online-backup copy is taken automatically before
  any pending EF migration (last 5 kept), plus manual "Create backup" / "Open backup folder"
  in Settings.
- **Faster startup.** Content seeding is skipped via a seed stamp (app version + content
  manifest + DB identity) once a fully successful pass has run; SQLite now runs in WAL mode.
- **AI availability banner.** Writing and Speaking hubs warn upfront when the Claude CLI is
  not found, instead of failing after a completed session.
- **Scoring regression tests.** 90+ table-driven tests covering `OverallBandCalculator`,
  `AnswerNormalization`, `TextAnswerChecker`, `IeltsWordCounter` and the official Cambridge
  band-conversion tables.

### Fixed
- **IELTS scoring correctness:** overall band no longer inflated by 0.5 for averages ending
  in .125/.625 (double rounding); digit-grouped answers (`1,000 kg`) and decimal commas
  (`3,5`) now match their keys; word-limit check no longer rejects listed acceptable
  answers; essay word counter treats `1,500` as one word.
- **Stability:** ~50 bugs found by a full code audit, including an infinite startup loop in
  the dictionary audio backfill, the Oxford 5000 import being skipped forever after AWL/AVL
  stubs, app crashes on the Listening/Reading result screens, a native Vosk use-after-free,
  a Claude CLI stdin/stdout pipe deadlock, lost answer flushes on test finish, session
  view-models and timers leaking (mic kept recording after closing a window via ✕), the
  maximize/restore caption button, mock-exam section navigation and writing-band weighting,
  and UTC/local-time mix-ups in stats and charts.
- Settings now apply without restart (`ClaudeCliPath`, SRS `TargetRetention`); language
  switching no longer duplicates dictionary filters or clears combo-box selections.

### Changed
- Seed services (Reading/Listening/Writing/Dictionary) now read from `IeltsContent\`
  instead of embedded resources, and soft-skip when their content is missing.
- Speaking import now reads from `IeltsContent\Speaking\` instead of a personal
  `Downloads\Telegram Desktop` path.
- Startup seeding is wrapped so a missing/failed section can no longer abort launch.

### Notes
- AWL/AVL word lists (CC0) remain embedded.
- The MIT license covers source code only; study material is not redistributed
  (see [Content & Legal](README.md#content--legal)).
