<!-- /.vuepress/components/BlogExcerpt.vue -->

<template>
<ul>
    <li v-for="post in posts" :key="post.path">
        <RouteLink :to="post.path">{{ post.date + ' ' + post.title }}</RouteLink>
    </li>
</ul>
</template>

<script setup>
import { computed } from 'vue'
import { useRoutes } from 'vuepress/client'
import { promotedPosts } from '../posts.js'

const routes = useRoutes()

const posts = computed(() => promotedPosts(routes.value, 5))
</script>
