# EnglishStudio

A Windows desktop app for learning English and preparing for **IELTS Academic** —
a vocabulary trainer that grew into a full exam simulator. Built with **WPF on .NET 10**.

> ⚠️ **Read [Content & Legal](#content--legal) before using or publishing this repository.**
> The app code is open source (MIT), but it relies on third-party copyrighted study
> material (Cambridge IELTS tests/audio, Oxford 5000) that is **not** licensed for
> redistribution and is **not** shipped with the source.

The user interface is in **Russian** (the app teaches English → Russian).

---

## Features

The app is organised as modules shown in a left sidebar:

| Module | What it does |
|---|---|
| 📖 **Словарь** (Dictionary) | ~6,250 words (Oxford 5000 + AWL/AVL + PHaVE) with IPA, UK/US audio, RU/EN definitions, examples, images; filters by CEFR / part of speech / topic / tag |
| 🎯 **Тренажёр** (Trainer) | Spaced-repetition trainer using a custom **FSRS-4.5** scheduler over words, phrasal verbs and collocations |
| 📊 **Статистика** (Stats) | Review counts, due cards, retention rate |
| 📚 **Reading** | Full IELTS Academic Reading tests (14+ question types), 60-min timer, band estimate, per-type breakdown |
| 🎧 **Listening** | IELTS Listening tests with real audio, 5 card types, answer scoring |
| ✍ **Writing** | Task 1 + Task 2 under one 60-min timer; AI band scoring (4 criteria) with RU/EN feedback and band-8 model answers |
| 🎤 **Speaking** | Part 1/2/3 flow with microphone recording, Whisper transcription, speech metrics (WPM/pauses/fillers/TTR) and AI band scoring |
| 🏁 **Полный экзамен** (Full Mock) | All four sections back-to-back from one Cambridge book, official overall-band rounding, per-section review |

AI grading (Writing/Speaking) is **optional**: it shells out to the **Claude CLI**.
Without it, the app still records attempts; only the AI score is unavailable.

---

## Tech stack

- **.NET 10** / WPF (`net10.0-windows`), pinned via `global.json` (10.0.102)
- **EF Core 10 + SQLite** (DB created at `%AppData%\EnglishStudio\dictionary.db`)
- **CommunityToolkit.Mvvm** (MVVM), Microsoft.Extensions.Hosting (DI)
- **NAudio** (recording/playback), **Whisper.net** (offline speech-to-text)
- **Claude CLI** subprocess for IELTS Writing/Speaking evaluation (optional)

See [`NOTICE`](NOTICE) for the full list of third-party libraries and data sources.

---

## Prerequisites

- **Windows 10/11** (WPF desktop app)
- **.NET 10 SDK** (10.0.102 or newer; `global.json` pins the version)
- *Optional* — **Claude CLI** on `PATH`, for AI grading of Writing & Speaking
- *Automatic* — on first Speaking/Pronunciation use, the app downloads a Whisper
  model (`ggml-base.en` ~142 MB, `ggml-medium.en` ~1.5 GB) into `%AppData%\EnglishStudio\Models\`

---

## Build & run

```sh
# from the repository root
dotnet build EnglishStudio.slnx
dotnet run --project src/EnglishStudio.App

# run the test suite
dotnet test
```

The database and any downloaded assets live under `%AppData%\EnglishStudio\`,
outside the repository.

A fresh clone builds and runs, but ships **without** IELTS tests and the Oxford
dictionary (that material is copyrighted — see below). Those sections show an
"import content" prompt until you load a content pack.

---

## Adding IELTS content (content pack)

The IELTS tests/audio and the Oxford 5000 dictionary are **not** part of the source.
You supply them as an external **content pack** and load it through the in-app importer:

1. Build a pack from your own legally obtained material with
   `dotnet run --project tools/ContentPackBuilder` (or obtain one).
2. In the app: **Настройки → 📦 Контент → Импортировать контент…**, or click
   **Импортировать контент** on any locked section. Pick the pack folder or `.zip`.
3. The content is copied into `%AppData%\EnglishStudio\IeltsContent\` and the database
   is (re)seeded. Re-importing is idempotent.

The open-licensed parts (AWL/AVL word lists, the spaced-repetition trainer) work
without any pack. Full layout, manifest schema, and builder usage are documented in
[`docs/CONTENT_PACK.md`](docs/CONTENT_PACK.md).

---

## Project structure

```
src/
├── EnglishStudio.App/                 WPF UI, ViewModels, themes, Shell
├── EnglishStudio.Modules.Dictionary/  words, DbContext, seed/services
├── EnglishStudio.Modules.Ai/          Claude CLI client + IELTS evaluators
├── EnglishStudio.Modules.Ielts.Core/  shared quiz infra + scoring
├── EnglishStudio.Modules.Ielts.Reading/
├── EnglishStudio.Modules.Ielts.Listening/
├── EnglishStudio.Modules.Ielts.Writing/
├── EnglishStudio.Modules.Ielts.Speaking/
├── EnglishStudio.Modules.Ielts.Mock/  full 4-section exam orchestrator
└── EnglishStudio.Content/             content-pack import orchestrator
tests/
└── EnglishStudio.Integration.Tests/   xUnit + EF Core SQLite in-memory
tools/                                 content-generation/import utilities
```

---

## Content & Legal

**The MIT license ([`LICENSE`](LICENSE)) covers the source code only.**

This project was built for personal, educational use. To work it needs IELTS
study material that is **third-party copyrighted** and is therefore **not part of
the public source** and **must not be redistributed**:

- **Cambridge IELTS 15–20** practice tests, audio recordings, and figures
  — © Cambridge University Press & Assessment
- **Oxford 5000** word list (definitions, examples, audio refs)
  — © Oxford University Press
- **PHaVE List** (phrasal verbs) — academic publication

These files are listed in [`.gitignore`](.gitignore) so they are **never committed**.
Your local build still works because MSBuild reads them from disk; they simply do
not enter Git history. Open-licensed seed data that *is* kept in the repo (AWL,
AVL — both CC0) and runtime-downloaded data (Tatoeba CC-BY, Whisper models) are
documented in [`NOTICE`](NOTICE).

> **Before making this repository public**, ensure no copyrighted study material is
> committed. "Educational purposes" is **not** a license to redistribute Cambridge/
> Oxford content. Each user must supply their own legally obtained materials.

The IELTS band-descriptor summaries used by the AI evaluators (`Modules.Ai/Rubrics/`)
are based on publicly available IELTS band descriptors; review them before publishing.

---

## License

[MIT](LICENSE) — see the note inside the file about third-party content scope.
