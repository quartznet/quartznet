// Controls for the link and anchor checker: a tree that must pass and a tree that must not.
//
// Run with: npm run docs:check-links-test

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { execPath } from 'node:process'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { path } from 'vuepress/utils'
import { checkDocs } from './check-links.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const fixture = (name) => path.join(here, 'check-links.fixtures', name)

test('a tree whose links all resolve is reported clean', async () => {
  const { problems, files, checkedLinks, checkedFragments } = await checkDocs(fixture('clean'))

  assert.deepEqual(problems, [], 'the clean fixture is the positive control; it must stay clean')
  assert.equal(files, 4)
  assert.equal(checkedLinks, 16, 'every link in the fixture is meant to be checked')
  assert.equal(checkedFragments, 10, 'every fragment in the fixture is meant to be checked')
})

test('headings are slugified the way the site slugifies them', async () => {
  const { pages } = await checkDocs(fixture('clean'))
  const slugs = pages.find((page) => page.file === 'slugs.md')

  // Not one of these is what GitHub's slugger — and so markdownlint's MD051 — would produce:
  // it deletes the punctuation that @mdit-vue/shared turns into a hyphen, and leaves a leading
  // digit alone. The last three are markdown-it-anchor's duplicate suffixes.
  assert.deepEqual(slugs.anchors, [
    'quartz-core',
    'jobdatamap-s-contents',
    'schedulebuilder-t',
    '_4-0-changes',
    'repeated-heading',
    'repeated-heading-1',
    'repeated-heading-2',
  ])
})

test('a page whose directory the router renames is routed the way the router routes it', async () => {
  const { pages } = await checkDocs(fixture('clean'))
  const routes = Object.fromEntries(pages.map((page) => [page.file, page.route]))

  assert.equal(routes['README.md'], '/', 'a README is the index of its directory')
  assert.equal(routes['nested/README.md'], '/nested/')
  assert.equal(routes['_drafts/note.md'], '/drafts/note.html',
    'sanitizeFileName strips the leading underscore, which is why docs/_posts is served at /posts')
})

test('a tree with dead links is rejected, one problem per link', async () => {
  const { problems } = await checkDocs(fixture('broken'))

  assert.deepEqual(problems.map(({ kind, href }) => `${kind}: ${href}`), [
    'dead anchor: slugs.md#quartzcore',
    'dead anchor: slugs.md#jobdatamaps-contents',
    'dead anchor: slugs.md#schedulebuildert',
    'dead anchor: slugs.md#40-changes',
    'dead anchor: slugs.md#repeated-heading-3',
    'no such page: nowhere.md',
    'no such page: _drafts/note.md',
    'no such file: assets/missing.txt',
    'dead anchor: #no-such-heading',
    'dead anchor: slugs.md#first-dead-anchor-in-a-table',
    'dead anchor: slugs.md#second-dead-anchor-in-a-table',
  ], 'the broken fixture is the negative control; every line of it must be caught')
})

test('a problem is reported on the line the link is written on', async () => {
  const { problems } = await checkDocs(fixture('broken'))
  const lines = Object.fromEntries(problems.map(({ href, line }) => [href, line]))

  assert.equal(lines['slugs.md#quartzcore'], 12)
  assert.equal(lines['#no-such-heading'], 20)
  // Two rows of one table: a block's line range is not good enough, the row has to be found.
  assert.equal(lines['slugs.md#first-dead-anchor-in-a-table'], 27)
  assert.equal(lines['slugs.md#second-dead-anchor-in-a-table'], 28)
})

test('a dead anchor is reported with the anchor that was probably meant', async () => {
  const { problems } = await checkDocs(fixture('broken'))
  const notes = Object.fromEntries(problems.map(({ href, note }) => [href, note]))

  assert.match(notes['slugs.md#quartzcore'], /closest: #quartz-core/)
  assert.match(notes['slugs.md#schedulebuildert'], /closest: #schedulebuilder-t/)
})

test('the command line exits non-zero when it finds problems', () => {
  const script = path.join(here, 'check-links.mjs')

  const clean = spawnSync(execPath, [script, fixture('clean')], { encoding: 'utf8' })
  assert.equal(clean.status, 0, clean.stdout + clean.stderr)
  assert.match(clean.stdout, /no dead links or anchors/)

  const broken = spawnSync(execPath, [script, fixture('broken')], { encoding: 'utf8' })
  assert.equal(broken.status, 1, 'CI relies on the exit code, not on the report')
  assert.match(broken.stdout, /11 problems/)

  const misused = spawnSync(execPath, [script, 'one', 'two'], { encoding: 'utf8' })
  assert.equal(misused.status, 2, 'a usage error is not a docs failure')

  const missing = spawnSync(execPath, [script, fixture('nowhere')], { encoding: 'utf8' })
  assert.equal(missing.status, 2, 'nor is being pointed at a directory that is not there')
  assert.match(missing.stderr, /no such directory/)
})
