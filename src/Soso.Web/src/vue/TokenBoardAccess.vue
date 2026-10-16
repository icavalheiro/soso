<script setup lang="ts">
import { computed, ref } from 'vue';
import { ActionIcon, Badge, Button, Group, MultiSelect, Stack, Text, Tooltip } from '@mantine-vue/core';
import { Save, Trash2 } from 'lucide-vue-next';
import type { Board, Token } from '../api';

const props = defineProps<{
    token: Token;
    boards: Board[];
    busy: boolean;
    onSave: ( boardIds: string[] ) => void;
    onRevoke: () => void;
}>();
const boardIds = ref<string[]>( props.token.boardIds );
const unchanged = computed( () => boardIds.value.length === props.token.boardIds.length && boardIds.value.every( id => props.token.boardIds.includes( id ) ) );
const options = computed( () => [
    ...props.boards.map( board => ( { value: board.id, label: board.name } ) ),
    ...props.token.boardIds.filter( id => !props.boards.some( board => board.id === id ) ).map( id => ( { value: id, label: 'Unavailable board' } ) ),
] );
</script>

<template>
  <Stack gap="xs" class="token-row">
    <Group justify="space-between" wrap="nowrap">
      <div style="min-width: 0;">
        <Text size="sm" :fw="500" style="overflow-wrap: anywhere;">{{ token.name }}</Text>
        <Text size="xs" c="dimmed">Expires {{ new Date(token.expiresAt).toLocaleDateString() }}</Text>
      </div>
      <Tooltip label="Revoke token">
        <ActionIcon :aria-label="`Revoke ${token.name}`" color="red" variant="subtle" :disabled="busy" @click="onRevoke()">
          <Trash2 :size="17" />
        </ActionIcon>
      </Tooltip>
    </Group>
    <MultiSelect
      label="Assigned boards" :aria-label="`Assigned boards for ${token.name}`" placeholder="No boards selected"
      searchable clearable
      :disabled="busy" v-model="boardIds" :data="options"
    />
    <Group justify="space-between">
      <Badge variant="light" :color="token.boardIds.length ? 'teal' : 'gray'">
        {{ token.boardIds.length ? `${token.boardIds.length} assigned` : 'No board access' }}
      </Badge>
      <Button size="xs" variant="light" :aria-label="`Save boards for ${token.name}`" :disabled="busy || unchanged" @click="onSave(boardIds)">
        <template #leftSection><Save :size="15" /></template>
        Save boards
      </Button>
    </Group>
  </Stack>
</template>
