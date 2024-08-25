import type { SidebarConfig } from "@vuepress/theme-default";

export const sidebarEn: SidebarConfig = [
  {
    text: "Getting Started",
    children: [
      "/documentation/quartz-3.x/quick-start",
      {
        text: "Tutorial",
        link: "/documentation/quartz-3.x/tutorial/",
        collapsible: true,
        children: [
          "/documentation/quartz-3.x/tutorial/using-quartz",
          "/documentation/quartz-3.x/tutorial/overview",
          "/documentation/quartz-3.x/tutorial/jobs-and-triggers",
          "/documentation/quartz-3.x/tutorial/more-about-jobs",
          "/documentation/quartz-3.x/tutorial/more-about-triggers",
          "/documentation/quartz-3.x/tutorial/simpletriggers",
          "/documentation/quartz-3.x/tutorial/crontriggers",
          "/documentation/quartz-3.x/tutorial/trigger-and-job-listeners",
          "/documentation/quartz-3.x/tutorial/scheduler-listeners",
          "/documentation/quartz-3.x/tutorial/job-stores",
          "/documentation/quartz-3.x/tutorial/scheduler-builder",
          "/documentation/quartz-3.x/tutorial/configuration-resource-usage-and-scheduler-factory",
          "/documentation/quartz-3.x/tutorial/advanced-enterprise-features",
        ],
      },
      "/documentation/quartz-3.x/configuration/reference",
      "/documentation/faq",
      "/documentation/best-practices",
      {
        text: "API Documentation",
        link: "https://docs.quartz-scheduler.net/apidoc/3.0",
      },
      "/documentation/quartz-3.x/db/index",
      "/documentation/quartz-3.x/migration-guide",
      "/documentation/quartz-3.x/miscellaneous-features",
    ],
  },
  {
    text: "How To's",
    children: [
      "/documentation/quartz-3.x/how-tos/one-off-job",
      "/documentation/quartz-3.x/how-tos/multiple-triggers",
      "/documentation/quartz-3.x/how-tos/job-template",
      "/documentation/quartz-3.x/how-tos/crontrigger",
      "/documentation/quartz-3.x/how-tos/rescheduling-jobs",
    ],
  },
  {
    text: "Packages",
    children: [
      {
        text: "Quartz Core Additions",
        children: [
          "/documentation/quartz-3.x/packages/quartz-jobs",
          "/documentation/quartz-3.x/packages/system-text-json",
          "/documentation/quartz-3.x/packages/json-serialization",
          "/documentation/quartz-3.x/packages/quartz-plugins",
        ],
      },
      {
        text: "Integrations",
        children: [
          "/documentation/quartz-3.x/packages/aspnet-core-integration",
          "/documentation/quartz-3.x/packages/hosted-services-integration",
          "/documentation/quartz-3.x/packages/microsoft-di-integration",
          "/documentation/quartz-3.x/packages/opentelemetry-integration",
          "/documentation/quartz-3.x/packages/opentracing-integration",
          "/documentation/quartz-3.x/packages/timezoneconverter-integration",
        ],
      },
      "/documentation/quartz-3.x/packages/quartz-3rd-party-plugins",
    ],
  },
  {
    text: "Unreleased Releases",
    collapsible: true,
    children: [
      {
        text: "Quartz 4.x",
        link: "/documentation/quartz-4.x/",
        children: [
          "/documentation/quartz-4.x/quick-start",
          {
            text: "Tutorial",
            link: "/documentation/quartz-4.x/tutorial/",
            children: [
              "/documentation/quartz-4.x/tutorial/using-quartz",
              "/documentation/quartz-4.x/tutorial/jobs-and-triggers",
              "/documentation/quartz-4.x/tutorial/more-about-jobs",
              "/documentation/quartz-4.x/tutorial/more-about-triggers",
              "/documentation/quartz-4.x/tutorial/simpletriggers",
              "/documentation/quartz-4.x/tutorial/crontriggers",
              "/documentation/quartz-4.x/tutorial/trigger-and-job-listeners",
              "/documentation/quartz-4.x/tutorial/scheduler-listeners",
              "/documentation/quartz-4.x/tutorial/job-stores",
              "/documentation/quartz-4.x/tutorial/configuration-resource-usage-and-scheduler-factory",
              "/documentation/quartz-4.x/tutorial/advanced-enterprise-features",
              "/documentation/quartz-4.x/tutorial/miscellaneous-features",
              "/documentation/quartz-4.x/tutorial/crontrigger",
            ],
          },
          "/documentation/quartz-4.x/configuration/reference",
          "/documentation/quartz-4.x/migration-guide",
          {
            link: "https://docs.quartz-scheduler.net/apidoc/4.0",
            text: "API Documentation",
          },
          {
            text: "How To's",
            children: [
              "/documentation/quartz-4.x/how-tos/one-off-job",
              "/documentation/quartz-4.x/how-tos/multiple-triggers",
              "/documentation/quartz-4.x/how-tos/job-template",
              "/documentation/quartz-4.x/how-tos/crontrigger",
            ],
          },
          {
            text: "Packages",
            children: [
              {
                text: "Quartz Core Additions",
                children: [
                  "/documentation/quartz-4.x/packages/quartz-jobs",
                  "/documentation/quartz-4.x/packages/json-serialization",
                  "/documentation/quartz-4.x/packages/quartz-plugins",
                ],
              },
              {
                text: "Integrations",
                children: [
                  "/documentation/quartz-4.x/packages/aspnet-core-integration",
                  "/documentation/quartz-4.x/packages/hosted-services-integration",
                  "/documentation/quartz-4.x/packages/microsoft-di-integration",
                  "/documentation/quartz-4.x/packages/opentelemetry-integration",
                  "/documentation/quartz-4.x/packages/opentracing-integration",
                  "/documentation/quartz-4.x/packages/timezoneconverter-integration",
                ],
              },
              "/documentation/quartz-4.x/packages/quartz-3rd-party-plugins",
            ],
          },
        ],
      },
    ],
  },
  {
    text: "Old Releases",
    collapsible: true,
    children: [
      {
        text: "Quartz 2.x",
        link: "/documentation/quartz-2.x/",
        children: [
          "/documentation/quartz-2.x/quick-start",
          {
            text: "Tutorial",
            link: "/documentation/quartz-2.x/tutorial/",
            children: [
              "/documentation/quartz-2.x/tutorial/using-quartz",
              "/documentation/quartz-2.x/tutorial/jobs-and-triggers",
              "/documentation/quartz-2.x/tutorial/more-about-jobs",
              "/documentation/quartz-2.x/tutorial/more-about-triggers",
              "/documentation/quartz-2.x/tutorial/simpletriggers",
              "/documentation/quartz-2.x/tutorial/crontriggers",
              "/documentation/quartz-2.x/tutorial/trigger-and-job-listeners",
              "/documentation/quartz-2.x/tutorial/scheduler-listeners",
              "/documentation/quartz-2.x/tutorial/job-stores",
              "/documentation/quartz-2.x/tutorial/configuration-resource-usage-and-scheduler-factory",
              "/documentation/quartz-2.x/tutorial/advanced-enterprise-features",
              "/documentation/quartz-2.x/tutorial/miscellaneous-features",
              "/documentation/quartz-2.x/tutorial/crontrigger",
            ],
          },
          "/documentation/quartz-2.x/configuration/",
          "/documentation/quartz-2.x/migration-guide",
          {
            link: "https://docs.quartz-scheduler.net/apidoc/2.0/html",
            text: "API Documentation",
          },
        ],
      },
      {
        text: "Quartz 1.x",
        link: "/documentation/quartz-1.x/",
        children: [
          {
            text: "Tutorial",
            link: "/documentation/quartz-1.x/tutorial/",
            children: [
              "/documentation/quartz-1.x/tutorial/using-quartz",
              "/documentation/quartz-1.x/tutorial/jobs-and-triggers",
              "/documentation/quartz-1.x/tutorial/more-about-jobs",
              "/documentation/quartz-1.x/tutorial/more-about-triggers",
              "/documentation/quartz-1.x/tutorial/simpletriggers",
              "/documentation/quartz-1.x/tutorial/crontriggers",
              "/documentation/quartz-1.x/tutorial/trigger-and-job-listeners",
              "/documentation/quartz-1.x/tutorial/scheduler-listeners",
              "/documentation/quartz-1.x/tutorial/job-stores",
              "/documentation/quartz-1.x/tutorial/configuration-resource-usage-and-scheduler-factory",
              "/documentation/quartz-1.x/tutorial/advanced-enterprise-features",
              "/documentation/quartz-1.x/tutorial/miscellaneous-features",
            ],
          },
          {
            link: "https://docs.quartz-scheduler.net/apidoc/1.0/html",
            text: "API Documentation",
          },
        ],
      },
    ],
  },

  {
    text: "License",
    link: "/license",
  },
];
