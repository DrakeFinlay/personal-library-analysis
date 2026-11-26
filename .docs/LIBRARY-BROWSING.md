# Browsing My Books and to-read groups

My Books initially opens **To read** in a compact **Table** with no cover images.
It loads the first 50 matching books, with the total shown above the table. This
keeps normal browsing small even when the library is large. Filters and sorting
apply to the entire library before those 50 books are chosen.

The default order is **To-read position**, ascending: 1, 2, 3, and so on.
Choose **Descending** to reverse it, or click the **Position** header again.
Blank positions remain last in both directions. The table also shows **Date
added** and can sort by that date, Title, or Author. Date added is the known
library-addition date, not the date of the latest edit or a later status change.

## Filter by status, shelf, and group

1. Choose a reading status on the far left: **To read**, **Read**, **Reading**, or
   another category. **All** includes all statuses.
2. Choose a shelf such as **history** in the **All shelves** menu above the table.
   Only books in both the selected status and shelf appear. Selecting **Read**
   now switches from To read history books to Read history books, keeping history.
3. Optionally select a **To-read group** to narrow the same results further.
   **All shelves** and **All to-read groups** remove their respective filters.

Choose **Show all books** to load every book matching the current filters;
**Show first 50** returns to the short list. Changing status, shelf, group, or
search returns to the first 50. Select All statuses and All shelves/groups before
Show all books if you want the whole library.

The search field is always visible above the filters. Enter a title, author, or
ISBN. **Entire library**, the default search scope, searches all saved books
before the 50-result limit, even if To read or a particular shelf/group is
selected. While text is entered, status, shelf, group, and missing-cover filters
are bypassed; **Show excluded records** still controls excluded books. Choose
**Current filters** to search only the selected status/shelf/group range instead.
Clear the search to return to browsing with the selected filters. Search does not
require loading all books first. **Show all books** expands the matching results.

Use the shelf and group menus for subject/project filtering. Sorting is available through
the Position/Date added/Title/Author headers or the sort controls. Drag column edges to resize them;
hover a truncated value to see the full title, shelf list, or group list.

Double-click a table row or select it and press Enter to open the book editor.
Closing the editor keeps your view. **Refresh library** reloads saved values while
keeping filters and the current result limit. **Covers** remains available with
the same filters and result limit. **Display…** controls the cover layout and fonts.
View and filter choices last for the app session; a new session starts at To read
and Table.

## Positions, dates, and Goodreads recovery

Open a book and edit **Book & editions → To-read position** or **Date added to
library**. Positions must be whole numbers starting at 1; leave unknown values
blank. Dates use `YYYY-MM-DD`. Save with **Save & close**. Position is relevant
to To read and is retained when the status changes. Equal ranks keep a stable
title order; editing a rank does not renumber the other books. Your local edits,
including a deliberately cleared rank/date, are preserved by later imports.

Goodreads imports read `to-read (#N)` from **Bookshelves with positions** and
**Date Added**. Positions on subject shelves or a separate Japanese To-read
shelf are not invented into a global rank. Missing or invalid values stay blank.
Existing source-managed values can update on later imports; local corrections
remain yours. A newly created local book gets today's date, while older books
without reliable source dates remain undated.

For older imports, open **Settings → Recover saved Goodreads order and dates…**.
The preview counts missing fields available in the Goodreads source fields
already stored on each book. Confirm to make a verified backup and fill those
fields only. It does not re-import statuses, ratings, reading records, notes,
covers, or ownership. It is safe to preview again; filled fields are not replaced.
The recovery itself is recorded in **Change history** in each book's editor.

## Assign your own groups

Open a book and enter names in **Book & editions → To-read groups (comma
separated)**, for example `Research for a book, Further biblical research`.
Existing names are suggested. Save with **Save & close**. A title may belong to
several groups independently of its subject shelves. Group membership survives
reading-status changes, imports, and book merges. Delete a name from the field
to remove that book from the group; clear the field to remove all its groups.
A group disappears from the menu when no books use it.

For several books, enable **Bulk edit**, select table rows using Ctrl-click or
Shift-click, enter a name beside **Add to group**, then **Apply to selected**.
In Covers, use the selection controls. **Select all shown** selects only loaded
rows, so the initial selection cannot silently include books beyond the first
50. Choose Show all books first if you deliberately want the entire filtered set.
Bulk group assignment adds to existing memberships; edit an individual book to
remove memberships. Commas separate group names in the individual editor.

The fast table avoids reading cover files. Other screens still load the records
needed for their calculations, and explicitly checking **Missing local cover**
must inspect cover-file availability. No personal-data benchmark has been run.
