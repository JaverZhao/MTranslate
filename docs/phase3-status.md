# Phase 3 Desktop UI status

Date: 2026-08-27

## Technology

- Avalonia Desktop 12.1.1 on .NET 10
- MVVM with CommunityToolkit.Mvvm 8.4.2
- Microsoft.Extensions.DependencyInjection composition root
- compiled XAML bindings
- Windows and macOS runtime path discovery

The visual system uses warm off-white surfaces, charcoal text, one-pixel neutral borders, restrained semantic pastels, crisp corners, and no gradients or heavy shadows.

The navigation now uses Fluent System Icons through `FluentIcons.Avalonia`. Home uses equal fixed-width source and target selectors with a dedicated swap column, and Settings uses a responsive full-width content stack with protected label/control columns so localized descriptions cannot collide with inputs.

## Pages

| Page | Current behavior |
| --- | --- |
| Home | Real local translation with live Standard/Q2_0C model selection through ModelManager, RuntimeManager, TranslationService, JobQueue, ChunkManager, and SQLite cache |
| Files | Shows the supported-format plan and an explicit Phase 4 parser boundary |
| History | Persists completed text translations in a dedicated SQLite table with search, copy, per-record deletion, and clear-all actions |
| Models | Shows Standard and Fast model cards, installed/runtime state, compatibility gate, and refresh |
| Local API | Shows the reserved loopback endpoint and explicit offline state until Phase 5 |
| Settings | Controls cache enablement, new-history persistence, interface language choice, and acceleration choice |

Home supports source and target selection using BCP-47 codes, model status, input and output character counts, translate, cancel, clear, copy, language/text swap, timing, cache-hit feedback, and `Ctrl+Enter`. Automatic source-language selection runs through `ILanguageDetector`; BCP-47 codes are converted to Hy-MT2's official prompt language names before inference.

The shared language catalog contains all 38 entries in the official Hy-MT2 language table. Home and Files use the same catalog for source and target selectors. Automatic detection covers distinct scripts plus lexical and orthographic profiles for related Latin, Cyrillic, Arabic-script, Chinese, and Devanagari languages.

## Acceptance

| Check | Result |
| --- | --- |
| Release build | Pass, 0 warnings and 0 errors |
| Core tests | Pass, 62 of 62 |
| Infrastructure tests | Pass, 11 of 11 |
| Desktop MVVM tests | Pass, 10 of 10 |
| Windows desktop process startup | Pass; process remained healthy after five seconds |
| Shutdown cleanup | Pass; no llama-server process remained |
| UI layout render check | Pass; Home and Settings captured at 1180 × 760 without overlap |
| Normal window close | Pass; desktop process exits after Gateway cleanup |

Desktop tests cover translation result/status updates, request language mapping, copy and clear behavior, language swapping, the complete language catalog, all six navigation destinations, single active navigation state, and propagation of the cache preference.

## Translation language regression

An early desktop build passed `auto` to the model as an omitted source language. Repeated testing of the reported English sentence reproduced target-language drift: one of eight runs echoed English and two of eight produced Thai. Explicit English source prompts produced Chinese in every run.

The correction adds heuristic source detection, maps internal BCP-47 codes such as `en`, `zh-CN`, and `zh-TW` to the official model prompt names, and changes the model profile cache identity so translations produced by the old prompt cannot be reused. Five post-fix real-model runs of the reported sentence through `en` to `zh-CN` all produced Simplified Chinese.

Document translation detects an automatic source language once from all translatable segments and reuses it for every line. This prevents short TXT lines from failing independently after the document language has been established. A real Q4 regression translated the five-line Turkish sample successfully while preserving every line boundary.

## Next phase boundary

Phase 4 will implement the document parser and translation pipeline in this order: TXT, SRT, VTT, Markdown, and ASS. The Files page intentionally does not offer a non-functional file picker before those parsers can preserve document structure safely.
