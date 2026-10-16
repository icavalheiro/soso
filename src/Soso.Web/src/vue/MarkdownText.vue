<script setup lang="ts">
import { computed } from 'vue';
import { renderMarkdown, type MarkdownMode } from './markdown';

const props = defineProps<{ source: string; mode: MarkdownMode; class?: string }>();
const emit = defineEmits<{ 'preview-image': [ id: string ] }>();

const html = computed( () => renderMarkdown( props.source, props.mode ) );

function onClick ( event: MouseEvent )
{
    const button = ( event.target as HTMLElement ).closest( '[data-image-id]' );
    const id = button?.getAttribute( 'data-image-id' );
    if ( id )
    {
        event.preventDefault();
        emit( 'preview-image', id );
    }
}
</script>

<template>
  <div :class="class" @click="onClick" v-html="html" />
</template>
