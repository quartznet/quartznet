import type { HeadConfig } from '@vuepress/core'

export const head: HeadConfig[] = [
    ['link', {rel: "apple-touch-icon", sizes: "180x180", href: "/apple-touch-icon.png"}],
    ['link', {rel: "icon", type: "image/png", sizes: "192x192", href: "/android-icon-192x192.png"}],
    ['link', {rel: "icon", type: "image/png", sizes: "32x32", href: "/favicon-32x32.png"}],
    ['link', {rel: "icon", type: "image/png", sizes: "96x96", href: "/favicon-96x96.png"}],
    ['link', {rel: "icon", type: "image/png", sizes: "16x16", href: "/favicon-16x16.png"}],
    ['link', {rel: "manifest", href: "/manifest.json"}],
    ['link', {rel: "shortcut icon", href: "/favicon.ico"}],
    ['meta', {name: "msapplication-TileColor", content: "#ffffff"}],
    ['meta', {name: "msapplication-TileImage", content: "/ms-icon-144x144.png"}],
    ['meta', {name: "theme-color", content: "#ffffff"}],
    ["script",
        {
            "data-ad-client": "ca-pub-2642923360660292",
            async: true,
            src: "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"
        }
    ]
];
