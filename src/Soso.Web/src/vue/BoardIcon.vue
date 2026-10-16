<script setup lang="ts">
import { computed, toRef } from 'vue';
import { boardIcons, useBoardColor } from './board-icon-data';

const props = defineProps<{ icon?: string; color?: string; size?: number }>();
const iconColor = useBoardColor( toRef( props, 'color', 'teal' ) );
const Icon = computed( () => boardIcons.find( option => option.value === props.icon )?.Icon ?? boardIcons[ 0 ].Icon );
</script>

<template>
  <Icon :size="size ?? 18" :color="iconColor" aria-hidden="true" />
</template>
