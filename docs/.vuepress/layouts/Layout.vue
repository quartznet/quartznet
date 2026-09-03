<!-- /.vuepress/layouts/Layout.vue -->

<!--
  The default theme's layout, plus one thing: a banner at the top of every page under
  `documentation/quartz-3.x/` saying which line the reader is in.

  It is a layout override rather than a note added to those pages because the 3.x tree is the 3.x
  line's live documentation and its page files are frozen here — see AGENTS.md. Keying the banner on
  the route means a page added to that tree on the 3.x branch is covered the day it lands, and
  nothing in the tree has to be edited from `main` to say it.
-->

<script setup>
import { computed } from 'vue'
import { RouteLink, usePageData } from 'vuepress/client'
import ParentLayout from '@vuepress/theme-default/layouts/Layout.vue'

/** The prefix every 3.x page is served under. */
const legacyPrefix = '/documentation/quartz-3.x/'

const page = usePageData()

const isLegacyVersion = computed(() => page.value.path.startsWith(legacyPrefix))
</script>

<template>
  <ParentLayout>
    <template #page-content-top>
      <div v-if="isLegacyVersion" class="custom-block warning">
        <p class="custom-block-title">Quartz.NET 3.x</p>
        <p>
          These are the documents for the 3.x line, which is maintained.
          <RouteLink to="/documentation/quartz-4.x/">Quartz.NET 4.x</RouteLink> is the current
          release; the
          <RouteLink to="/documentation/quartz-4.x/migration-guide.html">migration guide</RouteLink>
          says what changed and what to do about it.
        </p>
      </div>
    </template>
  </ParentLayout>
</template>
