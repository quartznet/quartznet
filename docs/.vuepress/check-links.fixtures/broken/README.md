---
title: Broken fixture
---

# Broken fixture

The negative control. Every link below is dead on a VuePress 2 site, and the checker is asserted
to say so — a checker nobody has watched go red is worth very little. The first four are the
anchors markdownlint's MD051 would ask for instead of the ones that work, which is why MD051 is
off and this runs.

- [GitHub deletes the dot; this site hyphenates it](slugs.md#quartzcore)
- [GitHub deletes the apostrophe; this site hyphenates it](slugs.md#jobdatamaps-contents)
- [GitHub deletes the angle brackets; this site hyphenates them](slugs.md#schedulebuildert)
- [GitHub leaves a leading digit alone; this site prefixes an underscore](slugs.md#40-changes)
- [one duplicate suffix past the last identical heading](slugs.md#repeated-heading-3)
- [a page that does not exist](nowhere.md)
- [a directory the router renames, reached by relative path](_drafts/note.md)
- [an asset that does not exist](assets/missing.txt)
- [a fragment this page does not have](#no-such-heading)

Links in a table are checked too, and are reported on their own row rather than on the row the
table starts at — most of the migration guide is one long table.

| 3.x | 4.x |
| --- | --- |
| [one dead anchor](slugs.md#first-dead-anchor-in-a-table) | `Quartz.Core` |
| [and another](slugs.md#second-dead-anchor-in-a-table) | `Quartz.Core` |
