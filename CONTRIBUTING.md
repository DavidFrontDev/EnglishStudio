# Contributing to EnglishStudio

Thanks for your interest in improving EnglishStudio! Bug reports, fixes, and features are welcome.

## ⚠️ Copyright — do not post third-party study material

EnglishStudio's source code is MIT-licensed, but the IELTS study material it uses is **third-party
copyrighted** and is **not** part of this project (see
[Content & Legal](README.md#content--legal) and [`NOTICE`](NOTICE)).

**Do not upload, commit, attach, paste, or link copyrighted study content anywhere in this project** —
including issues, issue comments, pull requests, discussions, or the source tree. This covers, but is
not limited to:

- Cambridge IELTS tests, audio recordings, transcripts, and figures (© Cambridge University Press & Assessment)
- The Oxford 5000 word list (© Oxford University Press)
- The PHaVE List of phrasal verbs
- IELTS band descriptors

This also includes **content packs** — and links to, or mirrors of, content packs — that bundle any of
the above. "For educational use" is **not** a licence to redistribute, and routing a link through a
third-party site does not change that.

Content that violates this rule will be removed without notice, and repeat violations may be reported to
GitHub.

### How content is meant to be supplied

Each user provides their **own legally obtained** materials and builds a local content pack with
`tools/ContentPackBuilder` (see [`docs/CONTENT_PACK.md`](docs/CONTENT_PACK.md)), then imports it from
inside the app. Content packs are never committed to, or distributed through, this repository.

## What is welcome

- Code: bug fixes, features, refactors
- Documentation and tests
- Open-licensed data **only** (e.g. CC0 / CC-BY) — always state the source and its licence

## Development

- Build: `dotnet build EnglishStudio.slnx`
- Test: `dotnet test`
- The app builds and runs without a content pack; IELTS sections and the Oxford dictionary show an
  "import content" prompt until a pack is imported.
