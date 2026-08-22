---
title: Clean fixture
---

# Clean fixture

Every link below resolves on a VuePress 2 site. Several of them are ones markdownlint's MD051
rejects, because it slugifies headings the way GitHub does rather than the way this site does.

- [a sibling page](slugs.md)
- [a slug where the punctuation became a hyphen](slugs.md#quartz-core)
- [a slug where an apostrophe became a hyphen](slugs.md#jobdatamap-s-contents)
- [a slug taken from a code span](slugs.md#schedulebuilder-t)
- [a slug prefixed because the heading starts with a digit](slugs.md#_4-0-changes)
- [the same slug written without the prefix, which VuePress adds for you](slugs.md#4-0-changes)
- [the second of three identical headings](slugs.md#repeated-heading-1)
- [the third of three identical headings](slugs.md#repeated-heading-2)
- [a link written without its extension](slugs)
- [a directory index](nested/)
- [the same directory index without its trailing slash](nested)
- [a site-absolute route](/slugs.html#quartz-core)
- [a page in a directory whose leading underscore the router strips](/drafts/note.html)
- [a fragment on this page](#clean-fixture)
- [an asset](assets/diagram.txt)

Links inside a table are checked too:

| 3.x | 4.x |
| --- | --- |
| [a slug from a code span](slugs.md#schedulebuilder-t) | `ScheduleBuilder<T>` |
