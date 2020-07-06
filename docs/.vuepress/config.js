module.exports = {
  title: 'Quartz.NET',
  description: 'A free, open-source scheduling framework for .NET.',
  head: [
      ['link', { rel: "apple-touch-icon", sizes: "180x180", href: "/apple-touch-icon.png"}],
      ['link', { rel: "icon", type: "image/png", sizes: "32x32", href: "/favicon-32x32.png"}],
      ['link', { rel: "icon", type: "image/png", sizes: "16x16", href: "/favicon-16x16.png"}],
      ['link', { rel: "manifest", href: "/site.webmanifest"}],
      ['link', { rel: "mask-icon", href: "/safari-pinned-tab.svg", color: "#3a0839"}],
      ['link', { rel: "shortcut icon", href: "/favicon.ico"}],
      ['meta', { name: "msapplication-TileColor", content: "#3a0839"}],
      ['meta', { name: "msapplication-config", content: "/browserconfig.xml"}],
      ['meta', { name: "theme-color", content: "#ffffff"}],
      ["script",
        {
          "data-ad-client": "ca-pub-2642923360660292",
          async: true,
          src: "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"
        }
      ]
    ],
  plugins: [
    '@vuepress/active-header-links',
    [
      '@vuepress/blog',
      {
        directories: [
          {
            // Unique ID of current classification
            id: 'post',
            // Target directory
            dirname: '_posts',
            // Path of the `entry page` (or `list page`)
            path: '/blog',
          },
        ],
        sitemap: {
          hostname: 'https://www.quartz-scheduler.net'
        },
        feed: {
          canonical_base: 'https://www.quartz-scheduler.net',
         },
      },
    ],
    '@vuepress/back-to-top',
    [
      '@vuepress/google-analytics', {
        'ga': 'UA-1433901-1'
      }
    ]
  ],
  themeConfig: {
    logo: '',
/*    algolia: {
      apiKey: 'e458b7be70837c0e85b6b229c4e26664',
      indexName: 'quartznet'
    },*/
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Blog', link: '/blog/' },
      { text: 'NuGet', link: 'https://nuget.org/packages/Quartz' }
    ],
    sidebarDepth: 1,
    sidebar: [
      {
        title: 'Documentation',
        path: '/documentation/',
        collapsable: false,
        children: [
          { 
            title: 'Quartz 3.x',
            path: '/documentation/quartz-3.x/',
            children: [
              ['/documentation/quartz-3.x/quick-start', 'Quick Start'],
              { 
                title: 'Tutorial',
                path: '/documentation/quartz-3.x/tutorial/',
                children: [
                  '/documentation/quartz-3.x/tutorial/using-quartz',
                  '/documentation/quartz-3.x/tutorial/jobs-and-triggers',
                  '/documentation/quartz-3.x/tutorial/more-about-jobs',
                  '/documentation/quartz-3.x/tutorial/more-about-triggers',
                  '/documentation/quartz-3.x/tutorial/simpletriggers',
                  '/documentation/quartz-3.x/tutorial/crontriggers',
                  '/documentation/quartz-3.x/tutorial/trigger-and-job-listeners',
                  '/documentation/quartz-3.x/tutorial/scheduler-listeners',
                  '/documentation/quartz-3.x/tutorial/job-stores',
                  '/documentation/quartz-3.x/tutorial/configuration-resource-usage-and-scheduler-factory',
                  '/documentation/quartz-3.x/tutorial/advanced-enterprise-features',
                  '/documentation/quartz-3.x/tutorial/miscellaneous-features',
                  '/documentation/quartz-3.x/tutorial/crontrigger'
                ]
              },
              ['/documentation/quartz-3.x/configuration/', 'Configuration Reference'],
              ['/documentation/quartz-3.x/migration-guide', 'Migration Guide' ]
            ]
          },
          { 
            title: 'Quartz 2.x',
            path: '/documentation/quartz-2.x/',
            children: [
              ['/documentation/quartz-2.x/quick-start', 'Quick Start'],
              { 
                title: 'Tutorial',
                path: '/documentation/quartz-2.x/tutorial/',
                children: [
                  '/documentation/quartz-2.x/tutorial/using-quartz',
                  '/documentation/quartz-2.x/tutorial/jobs-and-triggers',
                  '/documentation/quartz-2.x/tutorial/more-about-jobs',
                  '/documentation/quartz-2.x/tutorial/more-about-triggers',
                  '/documentation/quartz-2.x/tutorial/simpletriggers',
                  '/documentation/quartz-2.x/tutorial/crontriggers',
                  '/documentation/quartz-2.x/tutorial/trigger-and-job-listeners',
                  '/documentation/quartz-2.x/tutorial/scheduler-listeners',
                  '/documentation/quartz-2.x/tutorial/job-stores',
                  '/documentation/quartz-2.x/tutorial/configuration-resource-usage-and-scheduler-factory',
                  '/documentation/quartz-2.x/tutorial/advanced-enterprise-features',
                  '/documentation/quartz-2.x/tutorial/miscellaneous-features',
                  '/documentation/quartz-2.x/tutorial/crontrigger'
                ]
              },
              ['/documentation/quartz-2.x/configuration/', 'Configuration Reference'],
              ['/documentation/quartz-2.x/migration-guide', 'Migration Guide' ]
            ]
          },
          { 
            title: 'Quartz 1.x',
            children: [
              ['/documentation/quartz-1.x/tutorial', 'Tutorial'],
            ]
          },          
          '/documentation/faq',
          '/documentation/best-practices',
        ]
      },
      {
        title: 'License',
        path: '/license',
        collapsable: false,
      },
      {
        title: 'Installation',
        path: '/download',
        collapsable: false,
      },
      
    ],
    searchPlaceholder: 'Search...',
    lastUpdated: 'Last Updated',
    repo: 'quartznet/quartznet',

    docsRepo: 'quartznet/quartznet',
    docsDir: 'docs',
    docsBranch: 'master',
    editLinks: true,
    editLinkText: 'Help us by improving this page!'
  }
}
