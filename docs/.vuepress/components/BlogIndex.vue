<!-- /.vuepress/components/BlogIndex.vue -->

<template>
<div>
    <div v-for="post in posts">
        <h2>
            <router-link :to="post.path">{{ post.dateString + ' ' + post.frontmatter.title }}</router-link>
        </h2>

        <p>{{ post.frontmatter.description }}</p>

        <p><router-link :to="post.path">Read more</router-link></p>
    </div>
</div>
</template>

<script>
export default {
    computed: {
        posts() {
            return this.$site.pages
                .filter(x => x.id === 'post')
                .sort((a, b) => b.path.localeCompare(a.path))
                                .map(x => {
                    x.dateString = x.path.substring(1, 11);
                    return x;
                });
        }
    }
}
</script>