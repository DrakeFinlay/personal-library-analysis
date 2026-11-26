# Importing bibliographies and finding cited library books

## Import a bibliography from an EPUB

1. Open **Bibliographies → Import bibliography from EPUB…**.
2. Search for the book whose bibliography you want to add and choose **Continue**.
   If it is missing, add and save it in My Books first.
3. Choose **Choose EPUB…** and select the file, or paste its full path into
   **EPUB file**. The EPUB does not need a Calibre connection.
4. Choose **Analyze bibliography**. Review the extracted text and entry count.
   Analysis runs locally and does not save anything to the book.
5. Use **Review citations / local matches** to search the extracted entries and
   inspect possible matches in My Books. Close that review to return to the preview.
6. Choose **Back up and save bibliography**. The app verifies a complete profile
   backup, keeps an exact copy of the EPUB, and saves the citations under the
   source book in Bibliographies. The change also appears in the book's
   **Change history** button. Pasted/scanned Markdown and online
   bibliographies remain separate.

The same import button is available in a saved book's **Bibliography** tab.
If the book has a linked Calibre EPUB, its path is filled in automatically; you
can choose a different file. Each import requires analysis and an explicit save.
Closing the preview leaves the saved bibliography unchanged.

Reimporting replaces only the current EPUB bibliography. Earlier saved citation
text remains in Change history and the backup; retained source files stay in the
profile. If either the EPUB or saved bibliography changes after analysis, saving
is rejected: analyze again to review the current version.

Extraction recognizes dedicated bibliography/reference headings, including
**Bibliography of Works Cited**, and retains complete citation text even when
the title or author cannot be recognized. It does not scan arbitrary endnotes,
unlock encrypted content, or guarantee every publisher's layout. A file without
a recognized bibliography reports an error and preserves existing data. The
preview is read-only; use personal bibliography Markdown for manual corrections
or unsupported layouts. Local matches are suggestions, not confirmed links.
Unusually large editions are rejected and oversized or malformed individual
documents are reported as unreadable; check that count before saving.

## Import photos of bibliographies or chapter notes

1. Open **Bibliographies** in the top navigation and choose **Import bibliography
   photos…**.
2. Choose the book whose bibliography you photographed, then **Continue**. Add
   the source through **+ Add a book** first if it is missing from My Books.
3. Select one or more page images. Files scan in filename order: names such as
   `page-01.jpg`, `page-02.jpg` keep pages in order. PNG and JPEG are supported;
   other listed formats depend on the image codecs installed in Windows.
4. Scanning starts automatically. The app checks reading orientation, detects
   substantial text columns/pages, straightens each area and reduces shadows.
   Two detected pages scan separately from left to right. You do not need to drag
   corners, accept each photo, or choose ranges before the batch runs.
5. When boundaries are uncertain, the whole photo is scanned instead. Narrow
   facing-page fragments are excluded when a substantially wider page is found;
   the original stays available. Layout notices appear with the page's text.
6. Wait for offline Windows OCR, then check the combined text in the editor. **Cancel
   scan** stops the remaining pages and keeps successful pages in the draft.
   If a page fails, its filename and error are shown; successful pages remain.
7. Choose **Review citations / local matches** to inspect possible references
   and My Books matches. A note can yield several references; **Full note
   (review)** entries retain its entire text, including commentary, short forms,
   articles, and unrecognized references. Close the review to correct the draft.
8. Choose **Save bibliography**. The saved text appears under the source book in
   Bibliographies. The profile retains the original photos, selected/straightened
   PNG areas, crop coordinates, automatic rotation/layout evidence, raw OCR words
   and positions, and text revisions.
   These are included in profile backups and bibliography Change history records
   the retained evidence. None of these files is sent online.

Scanning appends to existing pasted/scanned text and does not replace an EPUB or
online bibliography. Saving requires your action. Closing an unsaved draft asks
whether to discard it. You can also open an existing text bibliography, choose
**Edit bibliography**, and use **Scan images…** to append pages.

**Adjust photo areas…** is an optional manual retry for a photo whose automatic
result is wrong. Select the problem photos, drag the four blue corners around a
page/text block, and use **Preview straightened page**. Leave margin around every
line. **Left page**, **Right page**, and **Whole photo** provide starting points;
**Reduce shadows** can be disabled. Use **Accept page & next** or **Skip photo**;
**Add another area** adds a second page or smaller block. Only this manual retry
requires accepting/skipping each selected area. Its result appends to the draft;
remove the unwanted earlier text section if replacing a bad scan.

OCR uses an installed Windows OCR language. Automatic detection respects EXIF and
compares four reading orientations using preview OCR. It groups line geometry
into text columns, follows drifting text boundaries, and adds margin around them.
The selected area is projected to a rectangle and normalized for uneven light.
Tall areas are also recognized in overlapping bands to reduce the effect of
curved baselines. The editor reconstructs wrapped paragraphs/numbered notes and
joins hyphenated line continuations; it keeps a photo/area heading for provenance.
Possible gaps in recognized note numbers and heavily slanted text are flagged
for review, without inventing missing content. Gaps can also exist in the source.
Automatic detection is heuristic: blur, severe page curvature, sparse text, or
unusual layouts can still need correction. It does not restore hidden/cut-off
text or guarantee publication details. Orientation detection adds processing time
per photo while removing the required per-photo selection work. No cloud OCR,
new downloaded model, or paid service is required.

Bibliography/note combinations are supported: full publication references can
be extracted from a note with several citations, while the full note remains
available. Known library titles can match despite missing OCR spaces when author
evidence is present. These remain suggestions, not confirmed book identities.
Review author initials, punctuation, italics, merged words and note boundaries.
OCR still makes spelling/number errors; perspective correction does not fully
flatten a curved book. Smaller selections can help. References split across
photos and ambiguous abbreviated references may require manual correction or
joining in the editor. No identities, missing words, or missing notes are guessed.
A failed match does not prevent saving or drop the text. Encrypted documents,
universal citation parsing, and automatic acceptance/creation of books are not
part of photo import. The source book must already be saved.

## See all cited titles that are in My Books

Choose **Bibliographies → Cited books in My Books**. This combines local EPUB,
saved online, and pasted/scanned bibliographies. It includes matching My Books
titles regardless of ownership, reading status, or source-book rating. Excluded
books are omitted. A title appears once, with evidence from its citing sources.

Filter by title or author, select a title, then use **Open book** or a **Cited by**
button. The original reference is shown so you can review a possible match.
Close and reopen this view to refresh it after bibliography or library edits.
Matching uses normalized titles (including subtitles) and available author
evidence; ambiguous records remain possible matches rather than accepted links.

## Explore citation chains and cross connections

Open **Bibliographies → Connections & citation stats**. This reads all saved EPUB,
online, and pasted/scanned bibliographies locally. It includes cited works outside
My Books. The graph is limited to works cited by at least two source books and
the source books that connect to them. Unrelated single-source leaf citations
are hidden. A source cited only once can still appear as a bridge in a deeper
chain. Nothing is changed by opening or exploring the graph.

An arrow means **source bibliography → listed work**. Incoming/outgoing details
spell out this direction. The original author may not have supplied the reference:
an edition can contain later introductions, editorial notes, or further reading.
For example, if a later source cites
a history book and that history book cites a poem, the two arrows form a chain.
Other sources citing the same poem form additional branches. A chain does not
mean that its first source directly cites every work farther along it.

1. Find a shared title or author in the left search box. Selecting a result
   immediately updates and fits the graph, highlights that work, and updates its
   details. No separate focus action is required. The initial view shows two steps.
2. Change **Steps** (1–5) to immediately update the current neighborhood. Clicking
   a graph node shows its details while keeping the expanded network. **Focus
   selected** can recenter the graph on that node.
3. Select a node, then **Expand selected** to add its immediate references and
   citing books. Existing branches stay visible, so you can expand several works
   and see their cross connections at the same time.
4. To trace between shared works, select the first, check **Trace from current
   focus**, then find/select the second in the left list. A shortest connection
   is highlighted in orange. Uncheck this option to return to immediate focusing.
   You can also select a visible graph node and choose **Trace from focus**.
   Paths can traverse arrows in either direction; each arrow keeps its actual
   citation direction. Other visible branches remain available.
5. Use the zoom slider and scrollbars to inspect the network. **Fit graph** fits
   the visible works together; focusing, expanding and tracing also refit the
   view. **Reset** returns to the focused work's chosen step count.
6. Select an arrow or a **Cited by / References** button in the details panel to
   read its evidence passages, source section and location. **Open source
   bibliography** opens the original reader. **Select citing work** and **Select
   cited work** let you continue exploring either end of that connection.

Light green nodes are **In My Books**, blue nodes are **Not in My Books**, and
amber nodes have a **Library match unresolved**. Each node also has a text label.
Colors describe library membership independently of physical ownership. External
works remain in chains when saved evidence supplies the connections; the app
does not automatically discover missing bibliographies.
Identities are inferred from citation titles and author evidence;
these are not accepted citation relationships. Ambiguous matches remain separate
instead of connecting arbitrarily to several library records. Matching can miss
OCR errors, translated titles, shortened names and variant titles. External
titles without author evidence are grouped within one source only, so incomplete
records can undercount repeated works. Text-only notes remain in their source
bibliographies and do not create invented work nodes.

Parts, volumes, translations and parent works are not automatically merged. For
example, citations naming **Inferno** and **Paradiso** remain those works, rather
than being relabeled as **The Divine Comedy**. Only identifiable saved citations
create edges; missing or unrecognized bibliographies cannot supply a chain yet.

The search lists shared cited works only, showing the first 100 matches if needed.
A focused view shows up to 120 works, and expansion up to 160. Hidden-neighbor
messages explain when a limit is reached; focus a narrower branch to continue.
All retained shared-network cross connections between visible nodes are drawn.
Shortest paths use the entire shared network. Statistics still use all saved
citations in the chosen attribution scope, independently of graph display limits. Close and reopen the
window after changing bibliography data to load a fresh snapshot.

## Review attribution and publication dates

Select an arrow or an incoming/outgoing connection in **Connections & citation
stats**, then choose **Review attribution / dates…** beside its evidence. Saved
EPUB/online citation readers also offer this button for a selected reference.

1. Inspect the reference and source section. Choose **Original-text citation**,
   **Editorial / introduction / translator material**, **Suggested / further
   reading**, or leave **Attribution unreviewed** when uncertain.
2. Optionally name the responsible contributor. Supply the **source edition year**
   for editorial/further-reading material if known. The source work's original
   year stays separate and is edited in the source book's normal editor.
3. Enter the cited work's **original publication year** separately from the
   **cited edition / reference year**. A blank original year uses a unique My Books
   match when known. A blank reference year uses a single year in the citation;
   multiple printed years remain unknown. Check these suggestions against the
   evidence; a printed year can describe a reprint rather than a new work.
4. Use **Check chronology**, then **Save attribution review**. The graph reloads;
   open the connection again to inspect the saved review. The source book's
   updated timestamp and Change history record the review.

A 1999 book listing a 1790 work's 1993 edition is chronologically possible.
A 1790 original text listing a work first written in 1999 needs review. An editor's
2005 bibliography may legitimately list that 1999 work. With an unknown original
date and only a later reference year, a qualified warning explains that ambiguity
instead of deleting the reference. No conflict detected does not verify unknown
dates or the attribution itself.

The default **all reference material** scope preserves all saved references.
Check **Reviewed original-text citations only** to include only references manually
classified as original text with no detected chronology warning. This changes both
the shared graph and repeated-work statistics. It may initially be empty: old
imports remain unreviewed, and section-based editorial/further-reading suggestions
are inferred rather than accepted reviews.

Reviews apply to individual evidence passages and source revisions, not entire
books or inferred work identities. Metadata is separate from imported evidence
and included in profile backups/history. Replacing an EPUB or changing pasted
bibliography text requires review of that new revision; old metadata is retained
but does not silently approve changed evidence. This build does not automatically
audit or relabel existing personal bibliographies.

## Statistics for repeatedly cited works

In the same window, select **Repeatedly cited works**. By default it lists works
cited by at least two distinct source books, sorted by that source count and
then by evidence count. Filter by title/author, or click column headings to sort.
Double-click a shared-work row or choose **Explore selected shared work** to open
its graph. Single-source rows cannot open a visualization.

- **Citing books** counts distinct source-book records, regardless of rating,
  ownership or reading status. Excluded books do not contribute.
- **Evidence passages** counts distinct citation text and attribution context
  within each source. Duplicate copies with the same context across EPUB/online/text
  count once for that source–target connection. Different attribution contexts
  remain separate evidence without adding another citing book.
  Several references in one note may point to different works; the full note can
  be the evidence for each. This is not an exact count of all printed mentions.
- **Also show repeated evidence within a single source** includes works with
  several distinct passages even if only one source book cites them. Those rows
  are available for statistics and do not enable graph exploration.

Counts cover your saved bibliography set in the selected attribution scope,
including inferred external works. All-reference counts include editorial and
further-reading material and are not proof of influence on the original author.
They do not measure worldwide citations or count indirect/transitive citations.
Different author/title variants can remain separate until the source is corrected
or a future identity-review workflow reconciles them. The original evidence stays
available for checking the grouping.
