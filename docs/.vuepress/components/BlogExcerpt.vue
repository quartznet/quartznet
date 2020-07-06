<!-- /.vuepress/components/BlogExcerpt.vue -->

<template>
<div>
    <ul v-for="post in posts">
        <li>
            <router-link :to="post.path">{{ post.dateString + ' ' + post.frontmatter.title }}</router-link>
        </li>
    </ul>
</div>
</template>

<script>
export default {
    computed: {
        posts() {
            return this.$site.pages
                .filter(x => x.id === 'post')
                .sort((a, b) => b.path.localeCompare(a.path))
                .slice(0, 3)
                .map(x => {
                    x.dateString = x.path.substring(1, 11);
                    return x;
                });
        }
    }
}
</script>