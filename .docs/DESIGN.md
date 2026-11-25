# Personal Library — design proposal

Date: November 3, 2026. Status: **Approved for implementation by the user.**

Actual completed work and validation are recorded in [IMPLEMENTATION.md](IMPLEMENTATION.md).

Working product name: Personal Library. Repository: `personal-library-analysis`.
The product name can change without changing its storage identity.

## 1. Purpose and decision status

Build a personal Windows desktop replacement for the parts of Goodreads that
support reading, remembering, and choosing books. Reading history is the primary
purpose; ownership is a separate attribute. The app works without internet access
and can use online services to enrich its locally stored catalog.

Confirmed requirements below come from the user's request, six visual references,
and subsequent answers. Items explicitly called **proposed defaults** are engineering
or behavior choices for review. Research suggestions are not additional user
requirements. In particular, the earlier research suggestion to inspect the real
Goodreads export immediately is superseded: development starts with synthetic data.

This document specifies the intended product and staged delivery, not completed
features or measured recommendation quality. The user approved beginning work on
November 3, 2026; proposed defaults are adopted unless subsequently revised.

## 2. Product boundaries

| Area | Agreed direction |
| --- | --- |
| Platform | Windows only, one computer and one local profile for the foreseeable future. |
| Launch | Standalone desktop app and Windows shortcut, following the neighboring projects. |
| Connectivity | Core functions offline; searches and background enrichment may use the internet. |
| Personal data | Local reading history, ratings, reviews, notes, vocabulary, preferences, and bibliographies. |
| Initial catalog | Small cached catalog, grown through lookups/imports; no large mandatory preload. |
| Resources | Up to 200GB is acceptable as long as that is valuable. Ideal is under 100GB of local storage used. |
| Hardware | User-reported 32 GB DDR5 RAM and RTX 4070; CPU and exact available VRAM not measured. |
| Appearance | Light mode primary; Goodreads-like warm off-white initially; dark mode also supported. |
| Imports | Goodreads CSV, Calibre refresh with a separate confirmed removal action, online lookup, and complete manual entry. |
| Library content | Primarily written works intended for linear reading. Reference, music, and photography/art books have separate categories. Manga and comics are outside the library scope. |
| Bibliographies | Persistent editable Markdown, source copies, matched references, and linked citation lists. |
| Recommendations | Ten To read picks, then ten physically owned To read picks. Shelf-based and offline catalog discovery follow. |
| Development | Randomized synthetic data first; personal import only at a later useful validation milestone. |

No friends feed, social network, hosted account, required cloud service, ebook
reader, Calibre replacement, multi-device synchronization engine, or manga/comics
recommendation experience is planned. Future Anki work concerns vocabulary; no
Anki writes or integration are included in the initial releases.

## 3. Screens and everyday use

Primary navigation: **Home, My Books, Recommendations, Bibliographies, Statistics,
Goals, Settings**. Later screens may be present only once their functionality is useful.

### Home

Currently reading occupies most of the useful space. Cards show the selected
edition's cover, title, author, page/percentage progress, and quick update controls.
Starting, finishing, or abandoning a reading is available from the card/details.
Use a responsive arrangement rather than reproducing Goodreads' narrow left rail.

Currently Reading offers editable, ordered sections on Home. Daily Reads is
available by default. Books without a section appear under Other books when
custom sections are in use. Move books from their cards and reorder them within
each section. Section membership is a personal preference on the book and
persists across rereads. Removing a section returns its books to Other books.

The sidebar shows **Recently changed books**: the ten most recently saved books
across reading statuses, excluding out-of-scope records. Reading updates, edits,
imports, and batch saves count as changes. Cards open the book editor.
Currently-reading books retain manual order. The Read next interface is retired.

### Goals

Goals is available in the top navigation. Its primary feature is a yearly list
of specific titles the reader chooses from My Books. Each title can occur once
per year and can be planned in multiple years. A completed reading in the selected
year marks that title complete once, including a reread or year-only completion.
Undated history does not count. Removing a title from a year preserves its book
and reading history. Imported updates preserve plans; merges combine both books'
planned years. Excluded books are hidden and special collections do not count as
completed reads. Add missing titles through the regular add-book workflow first.

The existing optional numeric annual reading challenge lives below the title
list, with its target editor, rather than on Home or in Settings. It counts all
eligible completed readings, independently of the title list.

### My Books and book details

My Books defaults to To read in a compact text table, with optional Covers.
Reading statuses appear on the far left, with separate shelf and to-read group
filters above the results. All is available for each filter. The table loads the
first 50 matching records; Show all books deliberately expands the filtered range.
Queries apply filters and sorting to the full library before limiting records.
The table virtualizes rows and loads no cover images. Optional title/author/ISBN
search lives under Find a specific book. Clicking a cover in bulk edit toggles its selection.
My Books keeps the current search, filters, selections, and scroll position when
the book editor closes. Refresh library reloads the saved records in that view.
The table defaults to numeric To-read position ascending, with a reversible
direction control. Unknown positions sort last in both directions. Position,
Date added, Title, and Author headers are sortable. Date added is an optional
library-addition date independent of the last-change timestamp and shelf changes.
Both fields are editable. Local corrections survive future imports. Goodreads
supplies the canonical to-read shelf's position and its Date Added field;
unrelated shelf positions are not global priority ranks. Recovery from saved
source fields fills missing values only after a verified backup and records the
changes. The table opens rows with a double-click or Enter.
Covers show available selected-edition images and placeholders. Cover Flow is later work.

User-named to-read groups overlap independently of shelves. The editor saves
comma-separated memberships and bulk edit can add a group to selected books.
Memberships survive status changes, imports, and merges. Select all shown targets
loaded rows; users must request Show all books to include a larger filtered set.

My Books saves the requested books per cover row and title and detail font sizes.
Narrow windows use fewer columns to keep cards usable. Bulk selection supports
Shift ranges in the displayed sort order in either direction. Add to shelf
suggests existing shelves and accepts new names. Configure selected opens a batch
metadata editor with explicit opt-in fields; edition metadata such as language
affects only each book's selected edition.

My Books offers a batch search for missing published covers and print MSRP.
Users can allow other editions' covers and comparable-edition prices. Preserve
source URLs and matched ISBNs, label alternate covers and estimated MSRP,
and disclose estimated items in purchase totals. Create a verified backup
before saving. Searches support cancellation and saving completed results.

Book details provide metadata/edition selection, status, shelves, ownership,
rating, reading history/progress, review, private notes, quotations, vocabulary,
and bibliography links. Sections can arrive in stages without changing identities.
New local entries can be created without an ISBN, cover, page count, or internet.

### Bibliographies

Open a bibliography from its source book or from a list of titles and authors.
The list uses two columns at narrower widths and three at wider widths. Closing
a bibliography keeps the list's scroll position. Add Bibliography attaches
pasted text or text recovered from page photos to a source book in My Books.
Review and edit the text before saving it as Markdown. A successful parser or
online match is never a prerequisite for saving.

Import bibliography photos is available directly on Bibliographies. Offline
Windows OCR supports multiple pages in filename order, cancellation, editable
review, and a preview of identifiable citations and local matches before saving.
Unrecognized text and original images are retained. Cited books in My Books
combines possible matches across EPUB, online, and pasted/scanned bibliographies,
deduplicates library works, and links each to its source evidence. All reading
statuses and ownership states are eligible; excluded records are omitted.

### Settings

Theme, profile/resource locations, backup/restore, source connections, background
job status, network controls, and installed resource versions. Provide **Open
profile folder** and visible information about missing optional services.

## 4. Books, editions, ownership, and shelves

A **work** groups an intellectual work for discovery, personal classification,
and recommendations. An **edition** represents a particular publication,
translation, format, ISBN, publisher, cover, and page count. Reading records
identify the edition used when known. Work/edition merges and corrections must preserve
reading history and source identifiers; never merge on title similarity alone.

Core metadata: title/subtitle, ordered contributors and roles, original publication
year, edition publication date, languages, identifiers, description, subjects,
format, page count, cover, and source/provenance. Optional edition measurements
include word count and physical thickness, with source, method, and whether the
value is supplied, extracted, or estimated. Unknown values remain unknown.
Original publication years must support BCE dates and approximate dates without
silently substituting the edition's year.

Ownership is independent of reading status: not owned, physical, ebook, and
audiobook, with multiple holdings possible. An imported Calibre record can provide
ebook ownership evidence without implying that the book was read or liked.
Audiobook holdings do not require time-based progress tracking.

Support unlimited overlapping user shelves, editable names, and membership across
many shelves. Keep status distinct from shelves internally, even when the UI
presents both together. Start with flat shelves. Nested subjects, historical
periods, regions, smart shelves, and mirroring granular Calibre metadata are
deferred until actual usage establishes the need. Preserve unmapped source
metadata for later mapping rather than creating a taxonomy automatically.

### Comics and manga

Skip imports with explicit comics or manga metadata or formats. The Goodreads
import report retains the skipped source rows. Confirmed comics and manga can
be removed from Personal Library while preserving the source files. Remember
their source IDs so imports and refreshes skip previously removed records.

Use explicit evidence for classification. An author, standalone volume number, or
vague subject is insufficient evidence. The user can mark additional records
excluded after import.

## 5. Reading history and personal writing

Statuses and collection categories include **to read, currently reading, read,
partially read, did not finish, reference only, music book, photography / art
book,** and **unclassified**. A book has one current status or category, and
multiple different books can be currently reading. Reference, music, and
photography/art books stay in My Books with their history and are outside reading
statistics and the current recommendations. Recommendations for similar items
in the specialized collections can follow later.

Each reading/rereading has its own optional start and finish dates, outcome,
edition, and progress entries. Work-level status summarizes the current state
without overwriting past readings. A reread creates a new reading record.

Every saved book change retains a timestamp and before/after snapshots in a
separate SQLite history table, accessible through Change history in the editor.
This covers metadata, classification, reading/progress/reread actions, imports,
merges, removals, and bibliography saves. Unchanged saves create no event.
Existing profiles begin at an explicitly labelled current-state baseline after
a verified migration backup; historical edits are not fabricated. History is
included in profile backups and stays out of normal paged browsing queries.

Books read before Goodreads may be marked read with unknown dates. The
`read-b4-goodreads` shelf is a migration input: transfer its books into the broader
read status with undated reading history, then remove the active legacy shelf.
Retain the original source value in import provenance for reconciliation. These books
appear in all-time read counts and a separate undated group, never in an invented
year. Unknown start dates, finish dates, page totals, and publication dates are
different missing values and must stay distinct.

Progress accepts page number or percentage. Convert between them when the selected
edition has a valid page count, while preserving what the user entered. A page
count change must not fabricate new reading activity. Page-number validation
allows correction of edition metadata rather than silently clamping an entry.
Finishing is an explicit action; entering 100% can offer it.

Ratings remain whole 1–5 stars, with unrated distinct from zero. Proposed default:
one current work rating/review, with revision timestamps; rereads retain independent
reading records without requiring another rating system.

Personal writing includes review, private notes, quotations, and vocabulary
entries. Quotations/vocabulary can include edition, page/location, context, and
optional explanation. Vocabulary capture comes before Anki integration, with stable
identities and exportable fields so later integration is possible. A quotation or
vocabulary note does not automatically create a review or influence recommendations.

## 6. Challenge and statistics

Annual challenge: a user-set whole-book goal for a calendar year. Page totals use
the full page count of a completed reading's edition in its completion year;
daily page logging is unnecessary. Partially read, DNF, reference-only, undated,
and excluded manga/comics do not count as annual completions.

**Proposed reread rule:** each completed reading, including a reread, counts once
toward that year's challenge/books/pages. Also show distinct works where useful.
This proposed default was adopted with approval of the design.
Completing the same reading twice must never double-count it.

The first Statistics release covers items 1–4. The two estimate-based totals
follow in a later release.

1. Books completed per year, with drill-through to the contributing readings.
2. Pages per year, with missing-page-count completions disclosed separately.
3. Publication-year scatterplot with solid dots at the saved coordinates.
   Completion date runs horizontally and original work publication year runs
   vertically. Selecting a dot opens its reading or the readings at that exact
   coordinate. Grid intervals adapt to the visible range and chart size.
   Double-click the plot to open it full screen. In full screen, the mouse
   wheel zooms the publication-year axis around the pointer while preserving
   the completion-date axis.
4. Lifetime total pages read, including completed readings with unknown dates.
5. Lifetime words read, with known/extracted counts and estimates distinguished.
6. Estimated height of the completed-book stack, with a simple visual and readable
   units such as centimeters, meters, or kilometers according to the total.

### Library purchases

Each saved edition can record an MSRP, a price paid, a vendor, and whether it
was bought new or used. All four are optional. MSRP and price paid each have
their own currency selector for CAD, USD, JPY, or EUR.

An edition can record multiple physical copies, each with its own optional
purchase year, price, vendor, and condition. A sold copy retains its purchase
history and can record optional proceeds and their currency. Selling the last
physical copy clears physical ownership while preserving digital ownership.

Purchased from suggests previously saved vendor names and accepts a new name.
Copies can be marked as gifts or library loans. Gift copies contribute to owned
retail value and are excluded from purchase spending and purchase breakdowns.
Library loans are excluded from ownership and financial statistics. Reading
history continues to include completed library readings.
Bulk edit can mark selected books Gift or Library alongside status and shelf
changes. Apply to each book's selected edition, targeting its latest unsold
physical copy when copy history exists. A Library action adds a loan entry if
all recorded copies have already been sold.

Lifetime purchase statistics include sold copies and report sale proceeds
separately from gross spending. Current library purchase statistics use only
held copies and editions. Each average uses the items included in its own
total. The edition's MSRP applies to each recorded physical copy. Foreign amounts use a dated Bank of Canada
rate snapshot, cached locally and refreshable from Statistics. CAD totals
disclose missing prices and amounts awaiting conversion. The two pie charts
count editions with a known condition or vendor, and report missing values
alongside each chart.

### Lifetime totals and playful scale statistics

Use completed reading records, including undated pre-Goodreads reads, for lifetime
totals. Follow the proposed reread rule above: each completed reading contributes
once, even if its work was read before. Offer a distinct-works view later if useful.
Exclude manga/comics and incomplete/reference-only readings from these completion
totals. The page total describes completed-book pages, not every page ever opened;
partial/DNF progress can become a separate statistic later. Ownership is irrelevant.

**Total pages:** sum the page counts associated with completed readings. Show how
many readings lack page counts rather than treating them as zero. Prefer the actual
edition used; an explicitly selected fallback must retain its provenance. A corrected
page count recalculates derived totals without creating another reading event.

**Total words:** feasible as a mixture of known counts and clearly labeled estimates;
complete exact coverage is not a requirement. Prefer a user-supplied or reliably
sourced edition count. Later, offer local extraction from a supplied ebook to count
its readable text. Extraction should identify its scope and omit navigation/duplicate
content where possible; indexes, notes, OCR errors, and language-specific word
boundaries can affect the result. A text-derived count measures the included text,
not proof that every word was individually read. ISBN/page metadata alone must not
be presented as an exact word count.

For editions lacking a count, estimate `pages × configurable words per page` when
page count is available. This is a coarse assumption, especially for illustrated,
poetry, reference, and multilingual books. Select an initial documented estimation
factor during implementation; allow per-book overrides and preserve the method/
version. Display **Estimated words read**, a known-versus-estimated breakdown, and
the number of readings with insufficient data. Do not invent a total for missing
pages or require full ebook text to use the app. Updating counts or estimation
settings refreshes totals. Local text extraction can arrive after the initial
page-based version.

**Book-stack height:** a playful physical equivalent of completed readings, including
ebooks and borrowed books; it does not describe the user's actual owned shelf.
Prefer the read edition's known/user-entered thickness. Otherwise estimate thickness
from `page count ÷ 2 × paper thickness per leaf + binding allowance`: a leaf has two
numbered page sides. Paper/binding assumptions are configurable, documented rough
defaults rather than claims about the actual edition. Use a chosen print-equivalent
page count for digital editions when available, otherwise disclose missing coverage.
Each reread adds another virtual copy under the same counting rule as page totals.

Sum in a consistent internal unit, then round sensibly for display; millimeter
precision is unnecessary. A compact stack graphic and a few sourced, optional
real-world height comparisons can make the result interesting. Keep the numerical
height available without comparisons. Label the total **Estimated book-stack
height** and show which readings use known thickness, page-based estimates, or
cannot contribute yet. These estimates work offline and do not require a large
dataset, language model, or automated measurement service.

Use signed chronological values for BCE/CE and display human-readable labels.
Unknown publication years are excluded from that axis with a visible count.
Readings with unknown completion year stay out of yearly plots. Year-only dates
may appear in their known year, visibly distinguished from exact-date positions;
undated and uncertain values must not receive fabricated exact dates. Repeated
readings can produce multiple points. Missing values must not appear as zero.

Reading highlights include the five most-read author credits by completed
readings and favorite author credits with an average rating of at least 4.5
stars across at least two distinct rated, completed books. The oldest completed
book uses its original publication year. The longest and shortest completed books use the edition page
counts recorded in reading history. Undated completions are eligible.

Broader subject, author, and language breakdowns follow later. Classification views must
explain overlapping membership so counts are not mistaken for mutually exclusive
totals. Cumulative books/pages-over-time charts are outside initial scope.

## 7. Adding books and importing sources

### Manual entry and online search

Search title, author, or ISBN locally first and offer online results when available.
Open Library is the first metadata adapter; other sources remain optional gap
fillers after checking their coverage and terms. Cache selected records and
download selected covers so saved books remain usable offline. A cache miss offline
offers manual entry and a pending lookup, not a blocked workflow.

Distinguish online results, cached catalog entries, and My Books. Discovering a
record does not automatically add it to a personal shelf or imply ownership.
Validate ISBNs, retain provider IDs, and show candidate editions when ambiguous.
User-corrected covers/page counts and other fields take precedence over subsequent
enrichment. Background fetches update only eligible fields and preserve provenance.

Use targeted API requests, source identification, throttling/backoff, and caches.
Future bulk catalog installation uses bulk data rather than harvesting APIs.
Specific quotas, model names, licenses, and resource sizes are checked when adopted.

### Goodreads CSV

Build the import pipeline with synthetic Goodreads-shaped exports first. Do not
assume all history or reread dates are available in the eventual export. Preview
source rows, mappings, duplicates, excluded records, and unresolved values before
committing an import transaction. Preserve source rows/IDs and an import report.

Map supported metadata, shelves, rating/review, dates, and reading state. Preserve
unknown columns for diagnosis; never fabricate absent history. Re-import is
idempotent for unchanged source records and previews conflicts with local edits.
Dates associated with read-before-Goodreads records require explicit mapping to
undated history rather than automatic annual placement. After that mapping, remove
the legacy `read-b4-goodreads` shelf from the active shelf list while retaining its
source value for repeat-import reconciliation.

The user approved Goodreads as the priority source for matching records. Apply
its status, rating, review, and source-managed shelves while preserving local
notes, bibliographies, covers, ownership, Calibre links, and separate local
reading history. Reuse exact Goodreads IDs, equivalent ISBNs, and unambiguous
title-and-author matches. Keep a separate edition when a matched work has a
different ISBN. Preview weaker matches for explicit resolution. Take a backup
before applying a reviewed batch.

### Calibre

Save the folder containing `metadata.db` in Settings. Refresh reads the local
catalog in a read-only SQLite transaction and previews additions and updates.
Applying saves the connection and updates the profile. Match by Calibre library
and book UUIDs. Retain previously imported records when the source disappears
and keep locally copied covers when source artwork is unavailable.

Import core metadata, copy covers, and link to Calibre's existing ebook files.
Keep source tags, identifiers, comments, publisher, and series in the Calibre
details. Leave personal shelves independent of those tags. Initially classify
new books as Unclassified. Use explicit comic formats or known manga/comic tags
to skip those titles on import. Refresh respects reading
history, personal fields, and edits to previously imported metadata.

My Books supports selecting results in List and Covers and applying one status
or adding a shelf to the selection. Bulk Read creates an undated completion
when the book has no completion history. An active reading has to be ended
through its reading controls before it can be reclassified.

Bulk removal offers a Personal Library-only action and an action for selected
books linked to the same Calibre library. The warning lists titles and formats.
The app backs up the profile first and rechecks the source identity, book folders,
and formats before calling Calibre's trash-backed removal. The app removes
local records after verifying their Calibre entries and active folders are gone.
Removing a Personal Library record suppresses its source ID during later refreshes.

## 8. Bibliography documents and citation relationships

Each bibliography belongs to a source work and optionally its particular edition.
Keep three layers: original supplied evidence, editable Markdown, and structured
references/matches. Plain-text paste has no external source file; retain its
initial text as the original evidence. Copy supplied EPUBs/PDFs/images into the
profile when document import is introduced, with original filename and hash.
Do not depend on the original external path remaining available.

Store each bibliography at a stable ID-based path, not a title-dependent filename.
Save Markdown atomically and keep revisions. The app's initial editor is the
supported editing path. External file edits must never be silently overwritten:
check the last-loaded content hash before saving and offer reload/save-as-conflict
copy when it changed. Automatic file watching and reconciliation come later.

Parsing produces references that can remain unresolved. Books are the primary
match target; preserve citations to articles, chapters, websites, and other material
as typed or unclassified references without forcing them into book records. Those
extra types are a proposed preservation default, not a promise of full indexing.

Confident identifier matches may be accepted automatically with provenance;
uncertain matches require user confirmation. A failed match never drops its text.
Reprocessing retains accepted user choices unless changed evidence invalidates
them, in which case surface a conflict rather than silently relinking.

Accepted references create directed edges: **source cites target**. Source book
details show References; target book details show Known citing works. Either may
exist outside My Books. Store evidence, source edition, reference location,
provider, match status, and retrieval/import dates. Preserve parallel evidence
without displaying duplicate logical connections.

The EPUB prototype extracts dedicated bibliography sections and preserves original
citation text, grouping, and source location in a separate local record. A searchable
browser offers possible matches by title and author. It was evaluated against the
complete Dominion bibliography. Image OCR uses the installed Windows recognizer
offline. Text-PDF extraction, scanned-PDF OCR, manual section selection, and
accepted citation edges remain later work.

For books without a local file, offer a user-initiated search of Internet
Archive's public editions. Preview an identifiable bibliography from the
chosen EPUB or public scan text, then save it with the edition link and source
hash. Preserve local EPUB citations and editable Markdown alongside it.

External incoming discovery may use OpenAlex/OpenCitations/Crossref after measuring
coverage. An empty result means no known indexed result, not no citations. A
citation is a relationship, not evidence of endorsement. Interactive network
visualization is an important later deliverable; relational linked lists come first.

## 9. Recommendations

The overview starts with up to ten of the strongest matches from **To read**, then
shows up to ten **owned and unread** matches. Each section shows ten covers across
at wide window sizes and five across in narrower windows. Each cover opens the
book editor. The owned section requires To read status and a currently held
physical copy. Ebook-only and audiobook-only holdings remain eligible only for
Top picks from To read. The owned section prefers books outside the first section when
there are enough matches. Each section uses at most one book per known author.
Not now hides a title for one year.
Not interested removes it from To read and hides it from recommendations while
retaining the library record and holdings. Both sections omit completed and
excluded books. Currently reading, DNF, partially
read, and reference-only books are outside these candidate groups. Per-shelf
recommendations and discovery from the offline reference catalog follow later.

**Cited by books you enjoyed** surfaces up to ten possible book references from
completed works rated four or five stars. It groups repeat citations and favors
references from multiple highly rated sources. Picks link to their evidence
and can include works outside My Books, which the reader can look up or add.

Begin with an explainable local baseline using explicit interests, subjects,
descriptions, ratings, and accepted citation relationships. Keep separate interests
rather than averaging unrelated tastes into one profile. An owned book is not
automatically liked; DNF is not automatically a negative rating.

Bibliography influence is adjustable, including zero. Direct connections to
relevant/valued source books can increase a candidate's rank; graph connectivity
alone must not dominate taste. Explanations state the evidence actually used,
such as a known citation or shared subject. Do not claim that a book is carefully
sourced based only on its promotional description.

Add locally computed text embeddings when the baseline and metadata justify them.
Ollama is the preferred optional runtime to evaluate, not a required background
service for the app. Store model identity/input hashes with embeddings; a missing
model falls back to the baseline. Local generative interpretation of reviews is
later work, with editable inferred preferences tied to supporting review passages.
Do not send personal reviews or the preference profile to a cloud model.

Optional later controls, still proposed: a temporary interest such as “medieval
history,” familiar-versus-exploratory selection, and dismissals with reasons such
as already read, wrong subject, or not now. The bibliography influence control is
confirmed; these additional controls await experience/user feedback.

Synthetic fixtures verify ranking mechanics and exclusion rules. They cannot
establish personal recommendation quality. After an explicitly authorized real-data
import, compare a simple baseline with review/citation additions, hold out history
without leaking held-out reviews, and obtain judgments on genuinely unread books.
No promise of outperforming Goodreads is made.

## 10. Technical design and local launch

**Selected proposal: C#/.NET 10, WPF, and SQLite through Microsoft.Data.Sqlite.**
Both inspected neighboring repositories use this Windows-native family. Reuse its
structure and operating conventions rather than introducing a browser server,
another desktop runtime, or a fork whose data model dictates this app's behavior.
No existing app fork has been selected or claimed to have been exhaustively assessed.

Use App, Core, Infrastructure, and executable Checks projects:

- App: WPF screens, view models, theme resources, navigation, and commands.
- Core: identities, reading/status rules, statistics, import reconciliation,
  recommendation scoring, and citation matching decisions.
- Infrastructure: SQLite, profile files, backup, source adapters, background jobs,
  optional model/extraction runtimes.
- Checks: meaningful synthetic domain/integration checks and isolated fixtures.

Use parameterized SQL, versioned migrations, transactions, and SQLite full-text
search where useful. Keep durable personal data separate from replaceable catalog
caches and indexes. Bounded citation traversal can use relational edges initially;
no graph database or vector server is required. Benchmark before selecting an
approximate vector index or changing database technology.

Background jobs are cancellable, resumable where appropriate, bounded, and visible
without blocking normal reading/editing. Work that waits for network/model access
must not hold long personal-database transactions. Prevent concurrent app instances
from modifying the same profile without coordination. Sanitize imported HTML and
treat reviews/documents/metadata as content, never executable instructions.

Proposed build/launch convention: PowerShell build/check/publish/launch scripts,
self-contained Windows x64 output under ignored `artifacts/PersonalLibrary`, and
a shortcut. The development shortcut builds current source incrementally before
launch and reports build errors rather than launching a stale executable. A
published app launches directly without requiring a development SDK. Use hidden
helper processes so no console is left open during normal use.

No cross-repo code or data is modified. Reuse of actual source code would require
checking its license and isolating any useful component first.

## 11. Profile, resources, Git, and backups

Proposed default layout:

```text
%LOCALAPPDATA%/PersonalLibrary/
  profile/
    personal.sqlite
    bibliographies/<id>/bibliography.md
    bibliographies/<id>/revisions/
    sources/<id>/
    covers/                         # retained covers, including user choices
    settings.json
  resources/
    catalog.sqlite
    embeddings/
    downloads/
  backups/
```

Model runtimes may use their own documented external storage. Large catalog data,
models, source files, profiles, caches, and backups are never shipped in Git. Keep
only small synthetic fixtures and necessary license notices in source control.
Downloaded resources have version/checksum metadata and are installed separately;
failed updates preserve the prior working resource. Optional resources cannot
block core startup. Provide resource-size visibility and prefer under 100 GB total
local storage; up to 200 GB is acceptable when it provides meaningful value.

Development uses an explicit `PERSONAL_LIBRARY_DATA_DIR` pointing to an isolated,
ignored directory, with a prominent test-data banner. Normal launch clears that
override. The synthetic generator uses reproducible random seeds and fictional
titles, contributors, reviews, covers, and bibliographies; it must not derive its
records from the user's actual screenshots/export. Online provider tests are
separate targeted integration checks.

Automatic backups include a consistent SQLite snapshot, Markdown/revisions,
original supplied evidence, personal covers, and settings, with versioned manifests
and checksums. Coordinate application writes and file capture so database and
document revisions agree; copying an active database file alone is insufficient.
Rebuildable catalog/model caches are excluded from normal personal backups.
Provide a complete portable profile export; optional resource inclusion can later
support a fully offline transfer without making each automatic backup enormous.

Proposed schedule: once per day after changes and before migrations/imports or
restore, retaining 14 daily backups and 4 weekly checkpoints. Reuse content-addressed
large evidence safely to avoid repeatedly copying unchanged PDFs; each retained
backup must remain independently restorable through the backup system. Show
backup failures and storage use; never report success on a partial archive.

Restore validates versions/checksums, previews contents, creates a pre-restore
backup, and validates the restored profile before replacing the active one. A
failed restore preserves the working profile. Keep the live SQLite profile local;
an optional user-selected backup destination can be cloud-synchronized. No live
multi-computer SQLite synchronization is promised.

## 12. Delivery stages and acceptance gates

### Stage A — first runnable core, synthetic data only

Deliver normal Windows launch, isolated synthetic profile, light/dark themes,
library search, list and cover views, and book/edition editing, ownership, flat shelves,
reading/rereading records, page/percentage updates, whole-star ratings, reviews
and private notes. Include the reading-focused Home, recently changed books,
yearly title lists and annual challenge in Goals, pasted Markdown bibliographies, and backup/
restore. Show covers in the Home sidebar and My Books. Book covers and page counts
can be set manually. Show a placeholder for books without a cover.

Acceptance: create a book offline, classify it, start/update/finish/reread it,
relaunch with data intact, and restore a profile containing its bibliography.
Undated reads stay outside yearly totals. Both library views and the Home sidebar
can be exercised using fictional books with cover images or placeholders.

### Stage B — migration-ready tracker, still synthetic during development

Add title/author/ISBN lookup and caching, synthetic Goodreads import preview/
reconciliation, read-only Calibre adapter, exclusion review, books/pages-per-year
and publication-year scatterplot, lifetime page totals, estimated word totals and
book-stack height, basic quotations/vocabulary capture, and
bibliography matching with References/Known citing works lists. Every importer
must support preview and preserve user edits before it touches personal sources.

Acceptance: mixed synthetic imports can be repeated without duplication, manga/
comics are excluded from all relevant metrics/signals, source failures preserve
prior data, and charts drill through to the right records. Calibre adapter checks
use synthetic exports/sources before any authorized read of the personal library.
Lifetime totals include undated completed readings without adding them to a year;
estimate labels and missing-data coverage agree with their contributing records.

**Real-data gate:** the user authorized Calibre as the first real source and
chose the normal profile on November 9, 2026. Verify the adapter on synthetic
fixtures before applying the authorized import with a pre-import backup.
Goodreads was authorized separately and requires the user's CSV export. Keep
personal data outside Git and regression fixtures.

### Stage C — useful personal recommendations

To read and owned-unread shortlists, adjustable bibliography boost, grounded
reasons, then local embeddings and review-derived preferences where evaluation
shows benefit. Validate mechanics synthetically first and taste quality with the
authorized personal profile. Add shelf-based and offline catalog discovery after
the initial shortlists are useful.

### Stage D — richer browsing and research tools

Sortable/bulk-editable table and keyboard/mouse Cover
Flow; ebook-derived word counts, more statistics and richer classification driven by use; EPUB/PDF/image
bibliography extraction and OCR; external Markdown change recognition; external
incoming-citation discovery; interactive citation graph; optional larger offline
catalog; later Anki vocabulary workflow. These are planned follow-ups, not silent
requirements for the first runnable release. Sequence them according to user
feedback; the citation graph remains an explicit later goal.

## 13. Verification and delivery practices

Use focused checks for work/edition identity, rereads, unknown dates, annual
attribution, page-count changes, exclusion propagation, import idempotency/conflicts,
legacy-shelf migration, lifetime totals with undated reads, word-count provenance,
stack-height units and leaf/page conversion, estimate and missing-data coverage,
user-field precedence, bibliography ambiguity/provenance, file conflicts, interrupted
updates, backup/restore consistency, and recommendation eligibility. Avoid tests
that merely mirror UI implementation. Verify visible UI in an isolated profile,
including keyboard focus, text scaling, light/dark readability, and no-network use.

Initial performance targets, to measure rather than promise: usable core startup
within 3 seconds on the target machine after installation, typical local library
search within 250 ms for 10,000 synthetic works, and responsive interaction during
background imports. Model warm-up and large resource processing are reported
separately from core startup. Record measured results and revise targets if needed.

Follow neighboring project conventions: `.docs/` for project documentation,
`codex/` branches for coherent changes, meaningful synthetic checks, self-review,
and reviewable PRs when publishing implementation work is authorized. Do not merge
without an explicit request. The first implementation delivery is prepared locally.

## 14. Review points and remaining evaluation

No further technical questionnaire is needed before reviewing this proposal.
Specific defaults to review are reread challenge counting, automatic backup
retention, flat initial shelves, and Stage A/B scope. The optional recommendation
controls are explanatory proposals.

Implementation will resolve actual Goodreads field coverage, Calibre adapter
compatibility, provider coverage/terms, OCR/parser selection, local model choice,
and measured resource/performance costs. Those are evidence-gathering tasks, not
reasons to require personal data or large downloads at project start.

## 15. Sources and inspected precedents

- User request, six screenshots, and answers in this project's November 3, 2026 chat.
- Earlier local research: `D:/Drake/git-projects/.brainstorming/2026-11-03-local-book-tracker/research-findings.md`.
  It supplies research context; its early real-data inspection step is superseded.
- Read-only neighboring-repository inspection: Anki Stats' `AGENTS.md`, development
  workflow, README, and launch script; Personal Japanese-to-English Dictionary's
  `AGENTS.md`, design sections, launch script, and ignore rules. Both use .NET 10/
  WPF/SQLite; their launch details differ, so this proposal states its own behavior.
- [Microsoft WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/):
  Windows desktop UI framework supporting the chosen platform direction.
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/):
  lightweight SQLite provider usable without Entity Framework.
- [Calibre command documentation](https://manual.calibre-ebook.com/generated/en/calibredb.html#remove)
  describes the trash-backed removal command used by the confirmed cleanup action.
- [Open Library API guidance](https://openlibrary.org/developers/api): targeted lookup,
  responsible caching/use, and separate bulk-data paths.
- [SQLite backup API](https://www.sqlite.org/backup.html): consistent database snapshot mechanism.
- [Ollama embeddings](https://docs.ollama.com/capabilities/embeddings): optional local
  embedding integration. No model or quality claim is committed by this design.

External documentation checked November 3, 2026. Recheck dependency versions and
applicable data/asset terms when implementing each integration.
