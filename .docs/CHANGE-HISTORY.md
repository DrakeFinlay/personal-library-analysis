# Book change history

Open a book from My Books or Home and select **Change history** at the bottom of
its editor. Select an entry on the left to see the saved before-and-after values.
The newest 100 entries load first; **Load older changes** retrieves earlier ones.
The viewer is read-only. It does not revert the book or save your current draft.

The history records book creation, saved metadata and personal-writing edits,
status changes, reading starts, progress, finishes, rereads, shelves/groups,
ownership/purchases, annual title plans, imports, and merges. Bibliography saves
also record their text/source changes and update the source book's change time.
New retained bibliography images are recorded even if the text stays the same.
Saving a citation attribution/date review also records the review metadata before
and after, updates the source book's change time, and appears in Recently changed.
Reviews are included in profile backups and remain separate from imported evidence.
Work-identity corrections, explicit identity merges and excluded interpretations
also record the shared identity map's before/after values on the source book.
Other bibliographies use that map when reopened; their raw source text and book
records are not rewritten. Profile backups restore the map with the evidence.
An unchanged Save does not create an event or move the book into Recently changed.
Unsaved typing is not recorded as a book change.

Changes are captured in SQLite transactions with the saved book, including the
specialized ratings update and removal paths. Book snapshots and attachment
revisions are kept outside the ordinary 50-book browsing results. Merging books
retains both histories under the surviving book; removed-book history remains in
the profile database even after the book disappears from My Books.

When an existing profile is first upgraded, the app makes and validates a
**pre-history-migration** backup and records **Tracking started** for its current
books. This is a starting snapshot taken now, not a claim that those books were
added or edited now. Historical reading dates stay in Reading & history, but
older metadata/status changes cannot be reconstructed from a current snapshot.

Backups include the change history. Restoring a backup returns both book state
and active history to that snapshot. The automatic pre-restore backup retains
the newer state/history if you need to return to it. Schema 1 backups remain
readable and are upgraded before subsequent writes; older app versions cannot
open the upgraded schema 2 profile. Keep using the updated launcher build.

History grows with actual saved changes and is not automatically pruned. External
edits to bibliography Markdown are still protected by the existing reload/conflict
checks; they are not monitored continuously or retroactively turned into events.
