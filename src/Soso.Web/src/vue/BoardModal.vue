<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ActionIcon, Button, Checkbox, Group, Modal, MultiSelect, Stack, Text, Textarea, TextInput, Tooltip } from '@mantine-vue/core';
import { ArrowDown, ArrowUp, Plus, Save, Trash2 } from 'lucide-vue-next';
import { api, newId } from '../api';
import type { Board, Column, Person } from '../api';
import { reportError } from './feedback';
import BoardIconPicker from './BoardIconPicker.vue';
import BoardColorPicker from './BoardColorPicker.vue';
import { useLanguage } from './language';

const props = defineProps<{
    board?: Board;
    onClose: () => void;
    onSave: ( board: Board ) => void;
    onDelete: ( id: string ) => void;
}>();
const { t } = useLanguage();
const name = ref( props.board?.name ?? '' );
const code = ref( props.board?.code ?? '' );
const description = ref( props.board?.description ?? '' );
const icon = ref( props.board?.icon ?? 'columns' );
const color = ref( props.board?.color ?? 'teal' );
const members = ref<string[]>( props.board?.members ?? [] );
const columns = ref<Column[]>( props.board?.columns ?? [] );
const people = ref<Person[]>( [] );
const busy = ref( false );
const confirmDelete = ref( false );
const boardId = computed( () => props.board?.id ?? '' );
const codeUpper = computed( {
    get: () => code.value,
    set: value => { code.value = String( value ?? '' ).toUpperCase().replace( /[^A-Z]/g, '' ).slice( 0, 10 ); },
} );

onMounted( () => { void api<Person[]>( '/people' ).then( result => { people.value = result; } ).catch( reportError ); } );

async function save ( event: Event )
{
    event.preventDefault();
    busy.value = true;
    try
    {
        const result = props.board
            ? await api<Board>( `/boards/${ props.board.id }`, 'PUT', { name: name.value, description: description.value, icon: icon.value, color: color.value, members: members.value, columns: columns.value, revision: props.board.revision, code: code.value } )
            : await api<Board>( '/boards', 'POST', { name: name.value, description: description.value, icon: icon.value, color: color.value, code: code.value || undefined } );
        props.onSave( result );
    }
    catch ( error )
    {
        reportError( error );
    }
    finally
    {
        busy.value = false;
    }
}

function reorder ( index: number, direction: number )
{
    const next = [ ...columns.value ];
    const target = index + direction;
    [ next[ index ], next[ target ] ] = [ next[ target ], next[ index ] ];
    columns.value = next;
}
</script>

<template>
  <Modal :opened="true" :title="t(board ? 'Board settings' : 'Create board')" centered size="md" @close="onClose">
    <form @submit="save">
      <Stack>
        <TextInput :label="t('Name')" required maxlength="80" v-model="name" />
        <TextInput
          :label="t('Ticket code')" :description="t('2 to 10 letters used in ticket ids, e.g. SOS-00001.')"
          required maxlength="10" v-model="codeUpper" placeholder="SOS"
        />
        <BoardIconPicker :value="icon" :color="color" :on-change="value => { icon = value; }" />
        <BoardColorPicker :value="color" :on-change="value => { color = value; }" />
        <Textarea :label="t('Description')" maxlength="2000" v-model="description" />
        <template v-if="board">
          <MultiSelect
            :label="t('Members')" searchable v-model="members"
            :data="people.filter(person => person.id !== board?.ownerId).map(person => ({ value: person.id, label: person.name }))"
          />
          <Text :fw="600" size="sm">{{ t('Columns') }}</Text>
          <div v-for="(column, index) in columns" :key="column.id">
            <Group gap="xs" wrap="nowrap">
              <TextInput
                :aria-label="`${t('Column')} ${index + 1} ${t('Name').toLowerCase()}`" :model-value="column.name"
                required maxlength="60" :style="{ flex: 1 }"
                @update:model-value="value => { columns = columns.map(item => item.id === column.id ? { ...item, name: String(value ?? '') } : item); }"
              />
              <Tooltip :label="t('Move up')">
                <ActionIcon :aria-label="t('Move up')" variant="subtle" :disabled="index === 0" @click="reorder(index, -1)">
                  <ArrowUp :size="16" />
                </ActionIcon>
              </Tooltip>
              <Tooltip :label="t('Move down')">
                <ActionIcon :aria-label="t('Move down')" variant="subtle" :disabled="index === columns.length - 1" @click="reorder(index, 1)">
                  <ArrowDown :size="16" />
                </ActionIcon>
              </Tooltip>
              <Tooltip :label="t('Remove column')">
                <ActionIcon :aria-label="t('Remove column')" variant="subtle" color="red" :disabled="columns.length === 1" @click="columns = columns.filter(item => item.id !== column.id)">
                  <Trash2 :size="16" />
                </ActionIcon>
              </Tooltip>
            </Group>
            <Checkbox
              :mt="7" size="xs" :label="t('Completed column')" :checked="column.isDone"
              @update:checked="isDone => { columns = columns.map(item => item.id === column.id ? { ...item, isDone: Boolean(isDone) } : item); }"
            />
          </div>
          <Button variant="subtle" size="xs" :disabled="columns.length >= 20" @click="columns = [...columns, { id: newId(), name: t('New column'), isDone: false }]">
            <template #leftSection><Plus :size="15" /></template>
            {{ t('Add column') }}
          </Button>
        </template>
        <Group justify="space-between">
          <Button v-if="board" color="red" variant="subtle" @click="confirmDelete = true">
            <template #leftSection><Trash2 :size="15" /></template>
            {{ t('Delete board') }}
          </Button>
          <span v-else />
          <Button type="submit" :loading="busy">
            <template #leftSection><Save :size="15" /></template>
            {{ t('Save board') }}
          </Button>
        </Group>
      </Stack>
    </form>
    <Modal v-if="board" :opened="confirmDelete" :title="t('Delete board?')" centered @close="confirmDelete = false">
      <Stack>
        <Text size="sm">{{ t('All tickets, comments and images in this board will be permanently deleted.') }}</Text>
        <Button
          color="red" :loading="busy"
          @click="busy = true; void api(`/boards/${boardId}`, 'DELETE').then(() => { onDelete(boardId); }).catch(reportError).finally(() => { busy = false; })"
        >
          {{ t('Delete permanently') }}
        </Button>
      </Stack>
    </Modal>
  </Modal>
</template>
