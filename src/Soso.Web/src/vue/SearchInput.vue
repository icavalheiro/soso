<script setup lang="ts">
import { ref, watch, type ComponentPublicInstance } from 'vue';
import { TextInput } from '@mantine-vue/core';
import { Search } from 'lucide-vue-next';
import { useLanguage } from './language';

const props = defineProps<{ onQuery: ( query: string ) => void }>();
const { t } = useLanguage();
const value = ref( '' );
const root = ref<ComponentPublicInstance | null>( null );
watch( value, current => { props.onQuery( current ); } );

function focus ()
{
    root.value?.$el.querySelector( 'input' )?.focus();
}

defineExpose( { focus } );
</script>

<template>
  <TextInput
    ref="root" class="topbar-search" :aria-label="t('Search tickets')" :placeholder="t('Search cards...')"
    size="xs" v-model="value"
  >
    <template #leftSection><Search :size="14" /></template>
  </TextInput>
</template>
