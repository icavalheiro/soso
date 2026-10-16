<script setup lang="ts">
import { toRef } from 'vue';
import { ActionIcon, Group, Stack, Text, Tooltip } from '@mantine-vue/core';
import { boardIcons, useBoardColor } from './board-icon-data';
import BoardIcon from './BoardIcon.vue';

const props = defineProps<{ value: string; color?: string; onChange: ( value: string ) => void }>();
const iconColor = useBoardColor( toRef( props, 'color', 'teal' ) );
</script>

<template>
  <Stack gap="6px">
    <Group gap="xs">
      <Text size="sm" :fw="500" id="board-icon-label">Icon</Text>
      <BoardIcon :icon="value" :color="color" :size="20" />
    </Group>
    <div class="board-icon-picker" role="group" aria-labelledby="board-icon-label">
      <Tooltip v-for="{ value: icon, label, Icon } in boardIcons" :key="icon" :label="label">
        <ActionIcon
          type="button" :aria-label="`Board icon: ${label}`" :aria-pressed="value === icon"
          :color="color" :variant="value === icon ? 'light' : 'default'" :size="36"
          @click="onChange(icon)"
        >
          <component :is="Icon" :size="19" :color="iconColor" />
        </ActionIcon>
      </Tooltip>
    </div>
  </Stack>
</template>
