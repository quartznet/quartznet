<!-- /.vuepress/components/BlogIndex.vue -->

<template>
<div>
    <div v-for="post in posts" :key="post.path">
        <h2>
            <RouteLink :to="post.path">{{ post.date + ' ' + post.title }}</RouteLink>
        </h2>

        <p v-if="post.description">{{ post.description }}</p>

        <p><RouteLink :to="post.path">Read more</RouteLink></p>
    </div>
</div>
</template>

<script setup>
import { computed } from 'vue'
import { useRoutes } from 'vuepress/client'
import { visiblePosts } from '../posts.js'

const routes = useRoutes()

const posts = computed(() => visiblePosts(routes.value))
</script>
