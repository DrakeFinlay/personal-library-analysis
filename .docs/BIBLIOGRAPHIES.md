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

## Import bibliography photos

1. Open **Bibliographies** in the top navigation and choose **Import bibliography
   photos…**.
2. Choose the book whose bibliography you photographed, then **Continue**. Add
   the source through **+ Add a book** first if it is missing from My Books.
3. Select one or more page images. Files scan in filename order: names such as
   `page-01.jpg`, `page-02.jpg` keep pages in order. PNG and JPEG are supported;
   other listed formats depend on the image codecs installed in Windows.
4. Wait for offline Windows OCR, then correct the text in the editor. **Cancel
   scan** stops the remaining pages and keeps successful pages in the draft.
   If a page fails, its filename and error are shown; successful pages remain.
5. Choose **Review citations / local matches** to inspect identifiable references
   and possible matches in My Books. Close the review to return to your draft.
6. Choose **Save bibliography**. The saved text appears under the source book in
   Bibliographies. The profile keeps the original scanned images and text revisions.

Scanning appends to existing pasted/scanned text and does not replace an EPUB or
online bibliography. Saving requires your action. Closing an unsaved draft asks
whether to discard it. You can also open an existing text bibliography, choose
**Edit bibliography**, and use **Scan images…** to append pages.

OCR uses an installed Windows OCR language and does not send images online.
Straight, well-lit, single-page photos work best. Review columns, hyphenation,
author initials, and titles carefully. A failed citation match does not prevent
saving, and unrecognized text stays in the editor and saved bibliography.
The review recognizes some printed/Markdown citation formats and can find known
library titles when OCR loses italics or wraps lines. It is not a universal
citation parser and does not automatically confirm identities or create books.

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
