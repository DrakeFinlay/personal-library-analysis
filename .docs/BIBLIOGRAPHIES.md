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
4. In **Select the text to scan**, drag the four blue corners around the target
   page or text block. Leave a little margin around all lines, especially hanging
   indents. Exclude facing-page fragments, fingers, and text on pages underneath.
   **Left page**, **Right page**, and **Whole photo** provide starting selections;
   adjust their corners to fit your photo. Selections are manual, not automatic
   page detection.
5. Use **Preview straightened page** to check that every line is included.
   **Reduce shadows** is on initially; turn it off if the preview makes fine
   type worse. Choose **Accept page & next** for each photo, or **Skip photo**.
   For a two-page spread or strongly curved page, **Add another area** accepts
   this selection and lets you select a second page/smaller block in the same
   photo. Areas scan in the order shown in the list. Every area must be accepted
   or skipped before **Scan selected pages** becomes available.
6. Wait for offline Windows OCR, then correct the text in the editor. **Cancel
   scan** stops the remaining pages and keeps successful pages in the draft.
   If a page fails, its filename and error are shown; successful pages remain.
7. Choose **Review citations / local matches** to inspect possible references
   and My Books matches. A note can yield several references; **Full note
   (review)** entries retain its entire text, including commentary, short forms,
   articles, and unrecognized references. Close the review to correct the draft.
8. Choose **Save bibliography**. The saved text appears under the source book in
   Bibliographies. The profile retains the original photos, selected/straightened
   PNG areas, crop coordinates, raw OCR words and positions, and text revisions.
   These are included in profile backups and bibliography Change history records
   the retained evidence. None of these files is sent online.

Scanning appends to existing pasted/scanned text and does not replace an EPUB or
online bibliography. Saving requires your action. Closing an unsaved draft asks
whether to discard it. You can also open an existing text bibliography, choose
**Edit bibliography**, and use **Scan images…** to append pages.

OCR uses an installed Windows OCR language. The selected area is orientation
corrected, projected to a rectangle, and optionally normalized for uneven light.
Tall areas are also recognized in overlapping bands to reduce the effect of
curved baselines. The editor reconstructs wrapped paragraphs/numbered notes and
joins hyphenated line continuations; it keeps a photo/area heading for provenance.
Possible gaps in recognized note numbers and heavily slanted text are flagged
for review, without inventing missing content. Gaps can also exist in the source.

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
My Books. Nothing is changed by opening or exploring the graph.

An arrow means **citing book → cited work**. For example, if a later source cites
a history book and that history book cites a poem, the two arrows form a chain.
Other sources citing the same poem form additional branches. A chain does not
mean that its first source directly cites every work farther along it.

1. Find a title or author in the left search box. Select it, then choose **Focus
   selected**. The initial view shows its immediate references and citing books.
2. Choose **Steps** (1–5) before focusing to load a wider neighborhood. Selecting
   another work shows its details without replacing your current focus.
3. Select a node, then **Expand selected** to add its immediate references and
   citing books. Existing branches stay visible, so you can expand several works
   and see their cross connections at the same time.
4. To trace between works, focus the first, find/select the second in the search
   results, and choose **Trace from focus**. A shortest connection is highlighted
   in orange. The path can traverse arrows in either direction; each arrow keeps
   the actual citation direction. Other visible branches remain available.
5. Use the zoom slider and scrollbars to inspect the network. **Fit graph** fits
   the visible works together; focusing, expanding and tracing also refit the
   view. **Reset** returns to the focused work's chosen step count.
6. Select an arrow or a **Cited by / References** button in the details panel to
   read its evidence passages, source section and location. **Open source
   bibliography** opens the original reader. **Select citing work** and **Select
   cited work** let you continue exploring either end of that connection.

Light green nodes are in My Books. Gray nodes are external works or ambiguous
library matches. Identities are inferred from citation titles and author evidence;
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

The search shows the first 100 matching works; refine it to find a specific title.
A focused view shows up to 120 works, and expansion up to 160. Hidden-neighbor
messages explain when a limit is reached; focus a narrower branch to continue.
All direct cross connections between visible nodes are drawn. These display
limits do not limit the statistics or shortest-path search. Close and reopen the
window after changing bibliography data to load a fresh snapshot.

## Statistics for repeatedly cited works

In the same window, select **Repeatedly cited works**. By default it lists works
cited by at least two distinct source books, sorted by that source count and
then by evidence count. Filter by title/author, or click column headings to sort.
Double-click a row or choose **Explore selected work** to open its graph.

- **Citing books** counts distinct source-book records, regardless of rating,
  ownership or reading status. Excluded books do not contribute.
- **Evidence passages** counts distinct citation text within each source. Duplicate
  copies across EPUB/online/text count once for that source–target connection.
  Several references in one note may point to different works; the full note can
  be the evidence for each. This is not an exact count of all printed mentions.
- **Also show repeated evidence within a single source** includes works with
  several distinct passages even if only one source book cites them.

Counts cover your saved bibliography set, including inferred external works.
They do not measure worldwide citations or count indirect/transitive citations.
Different author/title variants can remain separate until the source is corrected
or a future identity-review workflow reconciles them. The original evidence stays
available for checking the grouping.
