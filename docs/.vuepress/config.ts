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
        redirectPlugin()
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
