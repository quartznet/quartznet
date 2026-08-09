import { viteBundler } from '@vuepress/bundler-vite'
import { webpackBundler } from '@vuepress/bundler-webpack'

import { defaultTheme } from '@vuepress/theme-default'
import { defineUserConfig } from '@vuepress/cli'
import { docsearchPlugin } from '@vuepress/plugin-docsearch'
import { registerComponentsPlugin } from '@vuepress/plugin-register-components'
import { googleAnalyticsPlugin } from '@vuepress/plugin-google-analytics'
import { redirectPlugin } from '@vuepress/plugin-redirect'
import {head, navbarEn, sidebarEn} from "./configs";
import * as path from "path";
import { getDirname } from "@vuepress/utils";

const __dirname = getDirname(import.meta.url)

export default defineUserConfig({
    base: '/',

    title: 'Quartz.NET',
    description: 'Open-source scheduling framework for .NET.',

    bundler: process.env.DOCS_BUNDLER === 'webpack' ? webpackBundler() : viteBundler(),

    head: head,

    plugins: [
        googleAnalyticsPlugin({
            'id': 'UA-1433901-1'
        }),
        docsearchPlugin({
            appId: 'QEIS1H2X5Q',
            apiKey: '8b6fcbbb7ef15a278af143526ce8c529',
            indexName: 'quartz-scheduler'
        }),
        registerComponentsPlugin({
            componentsDir: path.resolve(__dirname, './components'),
        }),
        redirectPlugin({
            // Pages that moved or were removed keep their old URLs working.
            config: {
                // the cron syntax reference left the tutorial, and its stale how-to fork was deleted
                '/documentation/quartz-4.x/tutorial/crontrigger.html': '/documentation/quartz-4.x/cron-expressions.html',
                '/documentation/quartz-4.x/how-tos/crontrigger.html': '/documentation/quartz-4.x/cron-expressions.html',
                // JSON configuration moved from packages/ to configuration/
                '/documentation/quartz-4.x/packages/json-configuration.html': '/documentation/quartz-4.x/configuration/json.html',
                // Quartz.OpenTracing is dropped in 4.x
                '/documentation/quartz-4.x/packages/opentracing-integration.html': '/documentation/quartz-4.x/packages/opentelemetry-integration.html',
                // the miscellaneous-features grab-bag was split; plug-ins were its largest part
                '/documentation/quartz-4.x/tutorial/miscellaneous-features.html': '/documentation/quartz-4.x/packages/quartz-plugins.html',
            }
        })
    ],

    theme: defaultTheme({
        themePlugins: {
            activeHeaderLinks: true,
            backToTop: true,
        },
        logo: '/quartz-logo-small.png',
        locales: {
            '/': {
                navbar: navbarEn,
                sidebar: sidebarEn,
                sidebarDepth: 2,
                colorMode: 'auto',
            }
        },


        lastUpdated: true,
        repo: 'quartznet/quartznet',

        docsRepo: 'quartznet/quartznet',
        docsDir: 'docs',
        docsBranch: 'main',
        editLinkText: 'Help us by improving this page!'
    })
})
