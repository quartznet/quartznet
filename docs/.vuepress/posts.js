// The one place that knows how a post is recognised among the site's routes.
//
// `config.ts` puts what a listing needs into each post's route meta under `post`; both listing
// components read it back through `useRoutes()`. Newest first, and the path breaks a tie so that
// two posts released on the same day come out in a stable order rather than in whatever order the
// routes module happened to list them.

/**
 * Every published post, newest first.
 *
 * @param {Record<string, { meta: Record<string, unknown> }>} routes what `useRoutes()` returned
 * @returns {{ path: string, date: string, title: string, description: string }[]}
 */
export const visiblePosts = (routes) =>
    Object.entries(routes)
        .filter(([, route]) => route.meta?.post && !route.meta.post.hidden)
        .map(([path, route]) => ({ path, ...route.meta.post }))
        .sort((a, b) => b.date.localeCompare(a.date) || b.path.localeCompare(a.path))

/**
 * The posts the home page promotes, newest first.
 *
 * @param {Record<string, { meta: Record<string, unknown> }>} routes what `useRoutes()` returned
 * @param {number} count how many to take
 */
export const promotedPosts = (routes, count) =>
    visiblePosts(routes)
        .filter(post => post.promote)
        .slice(0, count)
