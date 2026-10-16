<script setup lang="ts">
import { computed, ref } from 'vue';
import { ActionIcon, Group, Modal, SegmentedControl, Stack, Text, TextInput, Tooltip } from '@mantine-vue/core';
import { Archive, ArchiveRestore, Search } from 'lucide-vue-next';
import { api, ticketBody, ticketKey } from '../api';
import type { BoardData, Ticket } from '../api';
import { reportError } from './feedback';
import { useLanguage } from './language';

const props = defineProps<{
    data: BoardData;
    onClose: () => void;
    onOpen: ( ticket: Ticket ) => void;
    onChange: ( ticket: Ticket ) => void;
}>();
const { t } = useLanguage();
const mode = ref( 'done' );
const search = ref( '' );
const busy = ref( false );

const doneColumns = computed( () => props.data.board.columns.filter( column => column.isDone ).map( column => column.id ) );
const archiveColumns = computed( () => [ ...props.data.board.columns, ...( props.data.board.removedColumns?.map( item => item.column ) ?? [] ) ] );

function visibleTickets (): Ticket[]
{
    const query = search.value.toLowerCase();
    return props.data.tickets.filter( ticket =>
    {
        const matchesMode = mode.value === 'archived' ? ticket.archived : !ticket.archived && doneColumns.value.includes( ticket.columnId );
        const matchesSearch = `${ ticket.title } ${ ticketKey( props.data.board, ticket ) }`.toLowerCase().includes( query );
        return matchesMode && matchesSearch;
    } );
}

async function toggle ( ticket: Ticket )
{
    busy.value = true;
    try
    {
        props.onChange( await api<Ticket>( `/boards/${ ticket.boardId }/tickets/${ ticket.id }`, 'PUT', { ...ticketBody( ticket ), archived: !ticket.archived } ) );
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
</script>

<template>
  <Modal :opened="true" :title="t('Archive list')" centered size="lg" @close="onClose">
    <Stack>
      <SegmentedControl v-model="mode" :data="[ { value: 'done', label: t('Done') }, { value: 'archived', label: t('Archived') } ]" />
      <TextInput
        :aria-label="t('Search archive')" :placeholder="t('Search tickets')" v-model="search"
      >
        <template #leftSection><Search :size="16" /></template>
      </TextInput>
      <Text v-if="visibleTickets().length === 0" size="sm" c="dimmed" ta="center" py="xl">
        {{ t(mode === 'done' ? 'No completed tickets' : 'No archived tickets') }}
      </Text>
      <Group v-for="ticket in visibleTickets()" :key="ticket.id" class="archive-row" wrap="nowrap">
        <button class="archive-title" @click="onClose(); onOpen(ticket);">
          {{ ticketKey(data.board, ticket) }} · {{ ticket.title }}<small>{{ archiveColumns.find(column => column.id === ticket.columnId)?.name }}</small>
        </button>
        <Tooltip :label="t(ticket.archived ? 'Restore ticket' : 'Archive ticket')">
          <ActionIcon
            :aria-label="t(ticket.archived ? 'Restore ticket' : 'Archive ticket')"
            variant="subtle" :disabled="busy" @click="toggle(ticket)"
          >
            <ArchiveRestore v-if="ticket.archived" :size="18" />
            <Archive v-else :size="18" />
          </ActionIcon>
        </Tooltip>
      </Group>
    </Stack>
  </Modal>
</template>
