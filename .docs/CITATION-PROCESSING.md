# Citation interpretation and work identities

The app processes saved citation evidence before matching it to My Books:

**Photo/EPUB text → interpreted references → work identities → library matches.**

A work can exist outside My Books and connect several source bibliographies.
Identifying a cited work does not add a book to your library or assert ownership.
Original-author attribution and chronology review remain separate from identity.

## Open the interpreted references

1. Choose **Bibliographies → Review processed citations…**.
2. Search for the source book, select it, and choose **Continue**. This combines
   its saved EPUB, online, and pasted/scanned evidence. Unsaved OCR drafts must be
   saved first to review persistent identities.
3. Select a reference. The right panel shows the source note, specific reference
   wording, printed note number, page/other locator, volume, edition year, and
   translator when recognized. Its explanation states how it was interpreted.
4. **Needs interpretation review** narrows the list to unresolved references and
   possible library matches that are not established work links. **With local
   matches** filters possible My Books matches. **Include complete notes** exposes
   the preserved whole-note rows; they do not create additional work connections.

Saved EPUB/online readers also show these interpretation details. A saved text
bibliography offers **Review saved interpretations…**. **Review citations / local
matches** in an unsaved OCR draft previews interpretation, but cannot save work
identity decisions until the source text is saved.

## What happens automatically

- Full publication references in supported note patterns are separated, including
  several references separated by semicolons, translation prose, or a page locator
  followed by another publication. Bibliography entries with recognizable author,
  title and publication details or Markdown title formatting are supported too.
- Phrases such as `For discussion, see` and `Trans.` are separated from names and
  titles. Recognized volume, translation contributor, edition year and page locator
  are retained separately. The original note text is always preserved.
- Author/short-title references resolve only against one earlier full reference
  in the same section. A short prefix shared by several works stays unresolved.
- `Ibid.` resolves only when the immediately preceding numbered note is present
  and contains one identified reference without unresolved references. Missing numbers, chapter
  changes, numbering resets and ambiguous antecedents prevent automatic linking.
  Photo boundaries alone do not reset the reference context.
- Deterministic title/creator identities group spelling-equivalent spacing,
  punctuation and case variants without needing a local book. Different full
  contributor names are kept separate; initials, shortened names and genuine
  spelling differences can require an explicit identity merge.
- Library matching happens after interpretation. A possible library title match
  alone does not establish an unresolved reference's identity or create a graph
  edge. Graphs, cited-library lists and citation recommendations use identifiable
  references and exclude unresolved or explicitly excluded interpretations.

This is a conservative pattern-based interpreter, not a universal citation-style
engine. Unrecognized text enters the review queue instead of becoming an invented
title. Existing structured EPUB/online title fields are adapted as inferred work
identities; their extraction can still be wrong and can be corrected or excluded.

## Correct, merge, or exclude a work

Select an individual reference and choose **Review work identity…**. This is also
available beside identified evidence in the connections graph.

- Correct **Canonical work title** and **Canonical author / contributor**, then
  choose **Save work identity**. All references using that inferred identity use
  the reviewed title/author on their next processing pass. The work's reviewed ID
  remains stable when its display title changes. Different OCR variants can still
  be separate identities until explicitly merged.
- To combine variants, choose an existing identity in **Merge into reviewed work**.
  The menu contains works previously reviewed in this app. The selected title and
  author fill in automatically. Saving redirects the old identity's aliases to
  the chosen identity across saved bibliographies. To merge two unreviewed variants,
  review the first, then choose it while reviewing the second.
- When a possible My Books match exists, **Use title / author from a possible
  My Books match…** can fill the canonical fields from that record. This is an
  identity suggestion, not a choice of physical ownership or a specific edition.
- For an unresolved reference, enter an identity only when the evidence supports
  it. The review applies to that source evidence; it does not teach the app to
  interpret every `Ibid.` as that work. A following consecutive `Ibid.` can then
  inherit the reviewed antecedent when its context is unambiguous.
- If an interpretation is not a work citation, check **Exclude this interpretation
  from work links (keep the evidence)** and save. Its text remains available, but
  it stops contributing graph links, cited-library results and recommendations.
- Cancel to leave uncertainty unresolved. Use profile backups to restore a map
  before a mistaken merge; there is no one-click split/unmerge interface yet.

Saving refreshes the reader. A resolved item disappears from the unresolved filter.
Other open views are snapshots: reopen them to see a correction made elsewhere.
The graph reloads immediately after an identity review made from its own evidence.

## OCR errors and provenance

`atBetrayer` and `at Betrayer` can share a work key when the other identity fields
agree. This does not silently change the photographed words. Correct the canonical
display title through identity review; use the text editor separately if you want
to correct the saved transcription. General spelling repair, invented missing
words, and unrestricted fuzzy merging are not performed.

Identity decisions live separately in the profile's `citation-work-identities.json`,
alongside evidence rather than inside My Books records. Saving records the map's
before/after values in the source book's Change history and updates its change time.
Profile backups include the map. Stale reviews and changed source revisions are
rejected. Identified-work aliases can intentionally apply across source revisions;
an unresolved passage decision requires that same evidence fingerprint.

Attribution reviews remain attached to evidence independently of identity display
corrections. Exact legacy attribution matches are retained; references newly split
or differently interpreted may require attribution review again. No saved source
photos, EPUBs or personal bibliography text are rewritten by opening this feature.

## Reading the statistics

**Citing books** counts each distinct source book once for a work. Several notes
and `Ibid.` references do not inflate that count. **Evidence passages** counts
distinct interpreted evidence contexts, not all printed mentions: duplicate copies
can collapse, OCR can miss references, and unresolved references are excluded.
Complete-note rows are not extra occurrences. Work parts, biblical-book/passages,
manuscript identities, comprehensive author-date styles and automatic cross-source
short-form resolution remain future extensions.
