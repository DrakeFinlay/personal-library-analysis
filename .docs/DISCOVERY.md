# Discover reading leads from your bibliographies

Open **Discovery** in the top navigation. It has two views:

- **Shared reading leads** finds works listed by at least two different source
  books in your saved bibliographies. It includes sources without a rating.
- **Cited by books you enjoyed** contains the citation suggestions previously on
  Recommendations: up to ten works from finished sources rated four or five stars.
  It can include a work cited by just one enjoyed source. Recommendations now
  keeps its To read and owned-unread sections.

Discovery reads saved EPUB, online, and pasted/scanned citation evidence locally.
The background load does not import new books or change your reading data. Save
an OCR draft first, then open Discovery or choose **Refresh discovery**.

## Find an intersection

1. In **Shared reading leads**, browse the text table or search a title/author.
   Searches cover all eligible leads before the first-50 display limit. Use
   **Show all leads** when more than 50 match; **Show first 50** returns to the
   bounded view. Column headings can sort the displayed table.
2. To focus on particular sources, expand **Focus on specific source books**.
   Click each source you want to require. Every selected book must list a work
   for that work to remain in the results. Click a source again to deselect it,
   or use **Clear source selection** to search all saved sources.
3. Collapse the optional source chooser again to give the table more room.
   Select a work to see its author, library membership, why it appears, and its
   citing source books. Source counts include all matching sources, not just the
   selected ones. Source choices/search apply to Shared reading leads only.
4. Use a source-book button to inspect its original reference. Use **Explore
   shared connections** to open the graph focused on the selected work and
   trace its other incoming/outgoing connections. The selected attribution scope
   carries into that graph.
5. **Open in My Books** opens an existing matched record. **Find or add book…**
   starts the regular lookup/add workflow for an external work; nothing is added
   just by viewing a discovery. An unresolved library match remains labeled as
   such and should be checked against the source interpretation before adding.

For example, if two chosen sources both list *Paul and the Faithfulness of God*,
that work appears once with both sources as evidence, even if it is absent from
My Books. Repeated footnotes in one source cannot make a work appear shared.

## Ranking and what is included

Shared leads rank first by the number of finished source books you rated four or
five stars, then by their total distinct citing-book count, then title. The
**Enjoyed sources** column is this explicit rating/history signal. Unrated or
lower-rated sources can still supply shared connections; ownership is not a
preference signal. This ranking identifies reading leads, not objective importance
or proof that the source authors endorse a work.

By default, local targets must be To read or Unclassified, with no completed
reading. **Include already-read books and other statuses** restores those works
for research/exploration. Deliberately dismissed, snoozed and excluded local
targets remain hidden. External works can appear without a local library record.

**Not now** uses the existing one-year recommendation snooze. **Not interested**
uses the existing dismissal behavior; for a local To read book this also removes
it from To read while retaining its record. External preferences stay in profile
settings. Existing citation recommendation dismissals/snoozes remain effective.
These preferences are shared with the enjoyed-books citation suggestions.

## Attribution and uncertain evidence

The default **all reference material** scope includes unreviewed references,
editorial material and further reading. Source evidence shows attribution labels.
Check **Reviewed original-text citations only** to require a manual original-text
review without a known chronology warning. This global scope applies to both
Discovery views and may initially be empty until references are reviewed.

Unresolved citation interpretations and non-book reference candidates do not
create shared reading leads. Different spellings, abbreviated authors or OCR
errors can still split one work into separate identities. Use
**Bibliographies → Review processed citations… → Review work identity…** to
correct or merge those identities, then refresh Discovery. See the
[citation processing guide](CITATION-PROCESSING.md).

Only saved, recognized references supply connections. Discovery does not search
the internet for missing bibliographies or infer a direct citation through an
indirect chain. The graph remains available for exploring those deeper chains.
Counts describe this saved bibliography set, not worldwide citation statistics.
Other open views are snapshots; refresh/reopen after source or identity changes.
