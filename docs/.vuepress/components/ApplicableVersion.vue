<!-- /.vuepress/components/ApplicableVersion.vue -->

<template>
<div class="custom-block tip">
    <p class="custom-block-title">
        Version compatibility
    </p>
    <p>
        This documentation relates to Quartz version {{ version }} and later.
    </p>
</div>
</template>

<script setup>
defineProps({
    /** The first Quartz version the page applies to. */
    version: {
        type: String,
        default: '3.0'
    }
})
</script>
