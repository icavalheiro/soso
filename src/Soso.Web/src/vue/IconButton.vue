<script setup lang="ts">
import { ActionIcon, Tooltip } from '@mantine-vue/core';

defineProps<{ label: string; expanded?: boolean; controls?: string }>();
defineEmits<{ click: [] }>();
</script>

<template>
  <Tooltip :label="label">
    <ActionIcon :aria-label="label" :aria-expanded="expanded" :aria-controls="controls" variant="subtle" color="gray" size="sm" @click="$emit('click')">
      <slot />
    </ActionIcon>
  </Tooltip>
</template>
