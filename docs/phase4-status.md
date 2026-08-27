# Phase 4 Documents status

Date: 2026-08-27

## Implemented formats

| Format | Protected structure |
| --- | --- |
| TXT | Encoding BOM, original CRLF/LF line endings, blank lines, indentation, trailing whitespace, and source file |
| SRT | Cue indexes, start/end timestamps, blank-line structure, and multiline cue boundaries |
| VTT | WEBVTT header, cue identifiers, timestamps, cue settings, NOTE, STYLE, and REGION blocks |
| Markdown | Fenced and inline code, URLs, link destinations, image destinations, HTML tags, front matter keys, and Markdown syntax |
| ASS | Sections, Format declarations, non-dialogue events, event fields, override tags, and newline/control escapes |

Each parser implements parse, structure-preserving write, and post-write validation through the common `IDocumentParser` contract.

## Translation pipeline

- Resolves parsers by extension without modifying TranslationService.
- Never overwrites the source or an existing destination.
- Translates subtitle cues in batches of up to 20 with stable segment IDs.
- Retries individual subtitle segments when batch markers are missing or malformed.
- Calculates progress from estimated source tokens rather than chunk count.
- Saves completed segment translations in versioned JSON checkpoints.
- Resumes a matching job by file SHA256, target language, model profile, and output path.
- Writes to `output.ext.tmp`, flushes, reparses, validates protected structure, then atomically renames.
- Supports translation-only, original-then-translation, and translation-then-original subtitle output.
- Detects source language per document segment when the UI is set to automatic.

## Desktop integration

The Files page supports native file selection, Avalonia 12 file drag-and-drop, multiple queued files, source and target language selection, subtitle output mode, output directory selection, token progress, pause with checkpoint preservation, continue/retry, and opening the output directory.

## Acceptance

| Check | Result |
| --- | --- |
| Release build | Pass, 0 warnings and 0 errors |
| Core tests | Pass, 30 of 30 |
| Infrastructure tests | Pass, 11 of 11 |
| Document format tests | Pass, 18 of 18 |
| Desktop tests | Pass, 7 of 7 |
| Real Q4 SRT translation | Pass, 2 cues in one batch |
| SRT indexes and timestamps | Pass, unchanged after real translation |
| TXT line layout regression | Pass, CRLF/LF, blank lines, indentation, trailing whitespace, and final newline preserved |

The real-model regression input is `tests/data/documents/phase4-real.srt`; the generated local output is `artifacts/phase4/phase4-real.zh-CN.srt`.

## Next phase boundary

Phase 5 will implement the authenticated loopback API Gateway. Document parsers remain independent from both the desktop views and API endpoints so future job endpoints can reuse the same safe pipeline.
