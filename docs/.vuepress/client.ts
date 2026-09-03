import { defineClientConfig } from 'vuepress/client'

import Layout from './layouts/Layout.vue'

// Replaces the default theme's `Layout` with the same layout plus the 3.x version banner. Every
// page that does not name a layout of its own uses `Layout`, so this is what puts the banner in
// front of the whole 3.x tree without touching a page in it.
export default defineClientConfig({
    layouts: {
        Layout,
    },
})
