# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
