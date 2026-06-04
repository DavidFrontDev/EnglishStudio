# Content Pack

The app code is open source, but the IELTS study material it uses (Cambridge IELTS
tests/audio/figures, Oxford 5000, PHaVE) is **third-party copyrighted** and is **not**
shipped with the source (see [Content & Legal](../README.md#content--legal)).

A **content pack** is the external bundle that carries this material. You build (or
obtain) it separately, then load it into the app through the built-in **importer**.
Until a pack is imported, the IELTS sections and the Oxford dictionary show an
"import content" prompt; the open-licensed parts (AWL/AVL word lists, the trainer)
work without it.

```
[Content Pack]  ──importer──►  %AppData%\EnglishStudio\IeltsContent\  ──seed──►  dictionary.db + media on disk
 (built/obtained               (canonical runtime root,                          (per-module tables + files)
  separately, never in git)     identical to the pack layout)
```

---

## Pack layout

A pack is a plain folder (or a `.zip` of that folder) with this structure:

```
EnglishStudio-Content/
├── manifest.json
├── Dictionary/
│   ├── oxford_5000.json
│   └── phave.json
├── Reading/
│   ├── ielts_reading_tests.json
│   └── <code>/<image>                       e.g. acad-r-test27/volcano_diagram.png
├── Listening/
│   ├── ielts_listening_tests.json
│   └── <code>/{<audio>.mp3, <image>, transcript.txt}
├── Writing/
│   ├── writing_tests.json
│   └── <code>/<image>
├── Speaking/
│   └── Ielts {book}/Test№{t}.txt            e.g. Ielts 15/Test№1.txt
└── Rubrics/                                 IELTS band descriptors used by AI grading
    ├── IeltsRubric_Writing.md
    └── IeltsRubric_Speaking.md
```

Media live in per-test `<code>/` subfolders because the seed services resolve assets
as `IeltsContent/<Module>/<code>/<relative>`. The importer copies the whole pack into
`%AppData%\EnglishStudio\IeltsContent\` verbatim, so the pack layout **is** the runtime
layout.

---

## `manifest.json`

```json
{
  "packVersion": 1,
  "createdAt": "2026-06-03",
  "sections": {
    "dictionaryOxford": true,
    "dictionaryPhave": true,
    "reading": true,
    "listening": true,
    "writing": true,
    "speaking": true,
    "rubrics": true
  }
}
```

- **`packVersion`** — pack format version (currently `1`). The importer accepts packs at
  or below the version it supports.
- **`createdAt`** — build date (`yyyy-MM-dd`, UTC).
- **`sections`** — which sections this pack provides. Keys are fixed:
  `dictionaryOxford`, `dictionaryPhave`, `reading`, `listening`, `writing`, `speaking`, `rubrics`.
  A section can be `false`/absent — packs may be partial (e.g. Reading-only), and the
  app gates each section independently.

The manifest is required; the importer validates that every section marked `true`
actually has its key JSON (or, for Speaking, at least one `.txt`).

---

## Building a pack

`tools/ContentPackBuilder` assembles a pack from the on-disk `Seed/` assets in your
working copy (the copyrighted files are git-ignored but present locally):

```sh
dotnet run --project tools/ContentPackBuilder -- \
  --out "C:\path\to\EnglishStudio-Content" \
  --speaking-src "C:\Users\<you>\Downloads\Telegram Desktop"
```

- `--seed-root <repo>/src` — optional; auto-located by walking up to `EnglishStudio.slnx`.
- `--out <folder>` — output folder (default `./EnglishStudio-Content`).
- `--speaking-src <folder>` — optional; root holding `Ielts {book}/Speaking/Test№{t}/Test№{t}.txt`.
  Omit to skip Speaking.

The tool copies each module's JSON, lays media out under `<code>/`, copies the Speaking
`.txt` files into `Speaking/Ielts {book}/Test№{t}.txt`, and writes `manifest.json`. Zip
the resulting folder if you want a single-file pack.

> The output is private copyrighted content — distribute it to users separately and
> **never commit it**.

---

## Importing a pack

In the app:

1. **Настройки → 📦 Контент → Импортировать контент…**, or click **Импортировать
   контент** on any locked IELTS/dictionary section.
2. Choose the pack **folder** or **`.zip`**.
3. Watch the progress bar (byte progress over the copy + re-seed phases).
4. On completion the importer reports per-section results; the hubs reload and the
   content becomes available.

Under the hood `ContentImportService`:
1. unzips to a temp folder if given a `.zip`;
2. validates `manifest.json` and the section files;
3. copies the pack into `%AppData%\EnglishStudio\IeltsContent\` (overwrite = update);
4. re-runs the idempotent seeders for the affected modules inside a fresh DI scope.

Re-importing is idempotent — it updates rather than duplicates.

> **Known limitation:** AWL/AVL word tags are seeded once at startup (they ship as CC0
> embedded data). Oxford words imported *after* startup won't carry AWL/AVL tags until
> the dictionary DB is rebuilt.

---

## Adding a new content section (for contributors)

When a future module needs its own copyrighted content, wire it through the same path
(see `plans/Infra_Publish_GitHub_AgentExecution.md` §9):

1. add a value to `ContentSection` and a key in `ContentManifest.KeyOf`;
2. add an `IsImported` case in `FileSystemContentStore`;
3. add a `<Module>/` branch to `tools/ContentPackBuilder`;
4. make the module's seed service read via `IContentStore` and gate on `IsImported`;
5. call its seeder from `ContentImportService`'s re-seed scope;
6. add `IsContentMissing` + `ContentMissingView` gating to its hub;
7. bump `manifest.packVersion` if the layout changed.
