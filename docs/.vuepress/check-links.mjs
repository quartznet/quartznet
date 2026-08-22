// Link and anchor checker for the VuePress 2 docs tree.
//
// markdownlint's MD051 validates fragments with GitHub's heading slugger, which is not the
// slugger this site uses, so it flags anchors that resolve and suggests anchors that 404.
// This checker asks VuePress itself instead: it builds the same markdown-it instance the site
// is rendered with (`createMarkdown` from `vuepress/markdown`, which wires markdown-it-anchor
// with @mdit-vue/shared's `slugify` and its `-1`/`-2` duplicate suffixes), so the ids it reads
// off the heading tokens are byte-for-byte the ids the published pages carry. Page routes are
// computed with VuePress's own `inferRoutePath` and `sanitizeFileName`, which is why
// `docs/_posts/x.md` is known to be served at `/posts/x.html`.
//
// Usage: node docs/.vuepress/check-links.mjs [docsDir]
//
// Exit codes: 0 clean, 1 problems found, 2 bad usage.

import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { createMarkdown } from 'vuepress/markdown'
import { inferRoutePath, isLinkExternal } from 'vuepress/shared'
import { fs, path, sanitizeFileName, tinyglobby } from 'vuepress/utils'

// The glob VuePress itself pages the source directory with (`resolveAppOptions`), so the checker
// covers exactly the files that become pages -- no more, no less.
const PAGE_PATTERNS = ['**/*.md', '!.vuepress']
const IGNORE_PATTERNS = ['**/node_modules', '**/.yarn', '**/.git', '**/.svn', '**/.hg']

// Extensions that are pages rather than assets. Everything else is checked as a file on disk.
const PAGE_EXTENSIONS = new Set(['', '.md', '.html'])

// The shape @vuepress/markdown's links plugin recognises as internal and rewrites into a route.
// Anything else it leaves as a plain href for the browser to resolve, which changes what the
// link is relative to -- see `resolveTarget`.
const VUEPRESS_INTERNAL_LINK = /^[^#?]*?(?:\/|\.md|\.html)(?:[#?].*)?$/

const decode = (value) => {
  try {
    return decodeURI(value)
  } catch {
    return value
  }
}

const decodeFragment = (value) => {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

/**
 * The route VuePress serves a source file at: `resolvePagePath` composed with `inferPagePath`,
 * with the frontmatter permalink winning when there is one. Routes are kept decoded so they
 * compare against link targets, which are decoded too.
 */
const routeOf = (filePathRelative, frontmatter) => {
  const permalink = typeof frontmatter?.permalink === 'string' ? frontmatter.permalink : null
  const pagePath = permalink ?? inferRoutePath(`/${filePathRelative}`)
  return decode(encodeURI(pagePath.split('/').map(sanitizeFileName).join('/')))
}

/**
 * Resolve a link target to a site-absolute path, from whichever base the site resolves it from.
 *
 * A link the links plugin recognises is rewritten at build time relative to the *source* file
 * path; one it does not recognise survives as a plain href and the browser resolves it relative
 * to the *route*. The two differ wherever `sanitizeFileName` rewrites a directory (`_posts` ->
 * `posts`) or a permalink moves the page, and a link written the first way into such a directory
 * is genuinely broken on the site.
 */
const resolveTarget = (page, rawPath) => {
  const from = VUEPRESS_INTERNAL_LINK.test(rawPath) ? `/${page.file}` : page.route
  return decode(new URL(encodeURI(rawPath), `http://docs${encodeURI(from)}`).pathname)
}

/** Levenshtein distance, used only to suggest what a dead fragment probably meant. */
const distance = (a, b) => {
  let previous = Array.from({ length: b.length + 1 }, (_, index) => index)
  for (let i = 1; i <= a.length; i++) {
    const current = [i]
    for (let j = 1; j <= b.length; j++) {
      current[j] = Math.min(
        previous[j] + 1,
        current[j - 1] + 1,
        previous[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1))
    }
    previous = current
  }
  return previous[b.length]
}

const suggest = (fragment, available) => [...available]
  .map((candidate) => ({ candidate, score: distance(fragment, candidate) }))
  .filter(({ candidate, score }) => score <= Math.max(3, Math.ceil(candidate.length / 3)))
  .sort((left, right) => left.score - right.score)
  .slice(0, 3)
  .map(({ candidate }) => `#${candidate}`)

/**
 * Walk the token stream a page parsed to, collecting the heading ids markdown-it-anchor assigned,
 * any ids written as raw HTML (VuePress renders that), and every link with the line it sits on.
 */
const readPage = (markdown, source, filePathRelative) => {
  const env = { filePathRelative, base: '/' }
  const tokens = markdown.parse(source, env)
  const lines = source.split(/\r?\n/)
  // The frontmatter plugin parses the body with the frontmatter cut off, so token line numbers
  // are short by however many lines it removed. Put them back on so reports cite the file.
  const offset = lines.length - String(env.content ?? source).split(/\r?\n/).length

  const anchors = new Set()
  const links = []

  const addHtmlIds = (html) => {
    for (const match of html.matchAll(/<[a-z][^>]*\sid=["']([^"']+)["']/gi)) anchors.add(match[1])
  }

  // markdown-it hands out a block's line range, not a per-link line, so walk the range for the
  // href itself and keep a cursor so repeated links on one line are reported where they are.
  const locate = (block, cursor, href) => {
    const first = (block?.[0] ?? 0) + offset
    const last = Math.min((block?.[1] ?? lines.length) + offset, lines.length)
    for (let line = Math.max(cursor.line, first); line < last; line++) {
      const column = lines[line].indexOf(href, line === cursor.line ? cursor.column : 0)
      if (column === -1) continue
      cursor.line = line
      cursor.column = column + href.length
      return line + 1
    }
    return first + 1
  }

  // Tokens arrive in document order, so one cursor over the whole page keeps `locate` monotonic.
  const cursor = { line: 0, column: 0 }

  const visit = (list, block) => {
    for (const token of list) {
      if (token.type === 'heading_open') {
        const id = token.attrGet('id')
        if (id) anchors.add(id)
      } else if (token.type === 'html_block' || token.type === 'html_inline') {
        addHtmlIds(token.content)
      } else if (token.type === 'link_open') {
        const href = token.attrGet('href')
        if (href) links.push({ href, line: locate(block, cursor, href) })
      }

      if (token.children?.length) visit(token.children, token.map ?? block)
    }
  }

  visit(tokens, null)

  return { anchors, links, frontmatter: env.frontmatter }
}

export const checkDocs = async (docsDir) => {
  const root = path.normalize(path.resolve(docsDir))
  if (!fs.existsSync(root)) throw new Error(`no such directory: ${root}`)

  // The site renders each heading wrapped in its own `#slug` permalink; those self-links are not
  // content and would drown the counts. Suppressing them leaves the ids untouched.
  const markdown = createMarkdown({ anchor: { permalink: false } })
  const files = (await tinyglobby.glob(PAGE_PATTERNS, { cwd: root, ignore: IGNORE_PATTERNS })).sort()

  const pages = new Map()
  const byRoute = new Map()
  for (const file of files) {
    const source = fs.readFileSync(path.join(root, file), 'utf8')
    const page = { file, ...readPage(markdown, source, file) }
    page.route = routeOf(file, page.frontmatter)
    pages.set(file, page)
    byRoute.set(page.route, page)
  }

  const problems = []
  let checkedLinks = 0
  let checkedFragments = 0

  const report = (page, line, kind, href, note) =>
    problems.push({ file: page.file, line, kind, href, note })

  for (const page of pages.values()) {
    for (const { href, line } of page.links) {
      // Anything with a protocol -- http, mailto, tel, data -- is somebody else's to verify.
      if (isLinkExternal(href)) continue
      checkedLinks++

      const hashAt = href.indexOf('#')
      const rawPath = (hashAt === -1 ? href : href.slice(0, hashAt)).split('?')[0]
      // VuePress rewrites a fragment that starts with a digit, matching what `slugify` does to
      // a heading that starts with one.
      const fragment = hashAt === -1
        ? ''
        : decodeFragment(href.slice(hashAt + 1)).replace(/^(\d)/, '_$1')

      let target = page
      if (rawPath) {
        const absolute = resolveTarget(page, rawPath)
        const extension = absolute.endsWith('/') ? '' : path.extname(absolute).toLowerCase()

        if (!PAGE_EXTENSIONS.has(extension)) {
          // An asset. It is served either from the docs tree or from .vuepress/public.
          const assets = [path.join(root, absolute), path.join(root, '.vuepress/public', absolute)]
          if (!assets.some((asset) => fs.existsSync(asset))) {
            report(page, line, 'no such file', href, absolute === href ? '' : `resolves to ${absolute}`)
          }
          continue
        }

        // A directory link may be written without its trailing slash; the static host redirects.
        const candidates = [inferRoutePath(absolute)]
        if (!extension) candidates.push(`${absolute.replace(/\/$/, '')}/`)
        const routes = [...new Set(candidates)]

        const resolved = routes.map((route) => byRoute.get(route)).find(Boolean)
        if (!resolved) {
          const wanted = routes.join(' or ')
          report(page, line, 'no such page', href, wanted === href ? '' : `resolves to ${wanted}`)
          continue
        }
        target = resolved
      }

      if (!fragment) continue
      checkedFragments++
      if (!target.anchors.has(fragment)) {
        const closest = suggest(fragment, target.anchors)
        report(page, line, 'dead anchor', href,
          `${target.file} has no #${fragment}` +
          (closest.length > 0 ? `, closest: ${closest.join(', ')}` : ''))
      }
    }
  }

  return {
    root,
    files: files.length,
    checkedLinks,
    checkedFragments,
    problems,
    pages: [...pages.values()].map(({ file, route, anchors }) => ({
      file,
      route,
      anchors: [...anchors],
    })),
  }
}

const run = async (argv) => {
  const args = argv.filter((argument) => argument !== '--')
  if (args.includes('--help') || args.includes('-h')) {
    console.log('Usage: node docs/.vuepress/check-links.mjs [docsDir]')
    console.log('Checks every internal link and heading fragment in a VuePress docs tree.')
    return 0
  }
  if (args.length > 1) {
    console.error('error: expected at most one argument, the docs directory')
    return 2
  }

  // Default to the tree this script lives in, so the npm script works from anywhere.
  const docsDir = args[0] ?? path.dirname(path.dirname(fileURLToPath(import.meta.url)))

  let result
  try {
    result = await checkDocs(docsDir)
  } catch (error) {
    console.error(`error: ${error.message}`)
    return 2
  }
  const { root, files, checkedLinks, checkedFragments, problems } = result

  console.log(`${path.relative(process.cwd(), root) || '.'}: ` +
    `${files} pages, ${checkedLinks} internal links, ${checkedFragments} fragments`)

  if (problems.length === 0) {
    console.log('no dead links or anchors')
    return 0
  }

  let current = null
  for (const problem of problems) {
    if (problem.file !== current) {
      current = problem.file
      console.log(`\n${path.relative(process.cwd(), path.join(root, current))}`)
    }
    console.log(`  ${String(problem.line).padStart(5)}  ${problem.kind.padEnd(12)}  ${problem.href}`)
    if (problem.note) console.log(`  ${' '.repeat(21)}${problem.note}`)
  }
  console.log(`\n${problems.length} problem${problems.length === 1 ? '' : 's'}`)
  return 1
}

if (process.argv[1] && path.normalize(process.argv[1]) === path.normalize(fileURLToPath(import.meta.url))) {
  process.exitCode = await run(process.argv.slice(2))
}
