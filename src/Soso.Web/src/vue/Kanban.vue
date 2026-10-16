<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import draggable from 'vuedraggable';
import { ActionIcon, Tooltip } from '@mantine-vue/core';
import { Plus } from 'lucide-vue-next';
import type { BoardData, Ticket } from '../api';
import TicketCard, { type CardNavKey } from './TicketCard.vue';
import { useLanguage } from './language';

const props = defineProps<{
    data: BoardData;
    tickets: Ticket[];
    onOpen: ( ticket: Ticket ) => void;
    onCreate: ( column: string ) => void;
    onMove: ( ticket: Ticket, columnId: string, position: number ) => Promise<void>;
}>();
const { t } = useLanguage();

const lists = reactive<Record<string, Ticket[]>>( {} );
const dragging = ref( false );
const dragged = ref<Ticket | null>( null );
const draggedWidth = ref<number | null>( null );
const overColumn = ref<string | null>( null );
const grabbedId = ref<string | null>( null );
const liveMessage = ref( '' );
const pointer = ref( { x: 0, y: 0 } );

function syncLists ()
{
    if ( dragging.value )
    {
        return;
    }
    const next: Record<string, Ticket[]> = {};
    for ( const column of props.data.board.columns )
    {
        next[ column.id ] = [];
    }
    for ( const ticket of props.tickets )
    {
        ( next[ ticket.columnId ] ??= [] ).push( ticket );
    }
    for ( const column of props.data.board.columns )
    {
        next[ column.id ].sort( ( left, right ) => left.position - right.position );
    }
    for ( const key of Object.keys( lists ) )
    {
        if ( !( key in next ) )
        {
            delete lists[ key ];
        }
    }
    Object.assign( lists, next );
}

watch( [ () => props.tickets, () => props.data ], syncLists, { immediate: true, deep: true } );

function positionBetween ( list: Ticket[], selfId: string, index: number ): number
{
    const items = list.filter( item => item.id !== selfId );
    const before = items[ index - 1 ]?.position;
    const after = items[ index ]?.position;
    if ( before === undefined )
    {
        return ( after ?? 2048 ) - 1024;
    }
    if ( after === undefined )
    {
        return before + 1024;
    }
    return ( before + after ) / 2;
}

function onStart ( event: { item?: Element } )
{
    dragging.value = true;
    const id = event.item?.getAttribute?.( 'data-ticket-id' );
    dragged.value = props.tickets.find( ticket => ticket.id === id ) ?? null;
    draggedWidth.value = event.item?.getBoundingClientRect?.().width ?? null;
    window.addEventListener( 'mousemove', trackPointer );
}

const overlayLeft = computed( () =>
{
    const left = pointer.value.x + 12;
    if ( draggedWidth.value === null )
    {
        return left;
    }
    return Math.max( 0, Math.min( left, window.innerWidth - draggedWidth.value - 12 ) );
} );

function trackPointer ( event: MouseEvent )
{
    pointer.value = { x: event.clientX, y: event.clientY };
    const hit = document.elementFromPoint( event.clientX, event.clientY )?.closest( 'section.kanban-column' );
    overColumn.value = hit?.getAttribute( 'data-column-id' ) ?? null;
}

function onEnd ()
{
    dragging.value = false;
    dragged.value = null;
    draggedWidth.value = null;
    overColumn.value = null;
    window.removeEventListener( 'mousemove', trackPointer );
}

function onChange ( columnId: string, event: any )
{
    const change = event?.added ?? event?.moved;
    if ( !change )
    {
        return;
    }
    const list = lists[ columnId ];
    const position = positionBetween( list, change.element.id, change.newIndex );
    void props.onMove( change.element, columnId, position );
}

function onGrab ( ticket: Ticket )
{
    grabbedId.value = ticket.id;
    liveMessage.value = `${ ticket.title } was lifted. Use arrow keys to reorder, space to drop, escape to cancel.`;
}

function onNavigate ( ticket: Ticket, key: CardNavKey )
{
    const fromIndex = props.data.board.columns.findIndex( column => column.id === ticket.columnId );
    if ( key === 'drop' || key === 'cancel' )
    {
        grabbedId.value = null;
        liveMessage.value = key === 'drop' ? `${ ticket.title } was dropped.` : `${ ticket.title } was released without moving.`;
        return;
    }
    if ( key === 'up' || key === 'down' )
    {
        const list = [ ...( lists[ ticket.columnId ] ?? [] ) ].sort( ( left, right ) => left.position - right.position );
        const index = list.findIndex( item => item.id === ticket.id );
        const target = index + ( key === 'up' ? -1 : 1 );
        if ( index < 0 || target < 0 || target >= list.length )
        {
            return;
        }
        const reordered = [ ...list ];
        const [ moved ] = reordered.splice( index, 1 );
        reordered.splice( target, 0, moved );
        lists[ ticket.columnId ] = reordered;
        const position = positionBetween( reordered, ticket.id, target );
        liveMessage.value = `${ ticket.title } was moved over droppable area ${ target + 1 } of ${ reordered.length }.`;
        void props.onMove( ticket, ticket.columnId, position );
        return;
    }
    const column = props.data.board.columns[ fromIndex + ( key === 'left' ? -1 : 1 ) ];
    if ( !column )
    {
        return;
    }
    const siblings = [ ...( lists[ column.id ] ?? [] ) ].sort( ( left, right ) => left.position - right.position );
    const before = siblings[ siblings.length - 1 ]?.position;
    const position = before === undefined ? 1024 : before + 1024;
    liveMessage.value = `${ ticket.title } was moved over droppable area ${ siblings.length + 1 } of ${ siblings.length + 1 } in ${ column.name }.`;
    void props.onMove( ticket, column.id, position );
}
</script>

<template>
  <div class="kanban">
    <section
      v-for="(column, index) in data.board.columns" :key="column.id" :data-column-id="column.id"
      :class="`kanban-column ${overColumn === column.id ? 'drop-target' : ''}`"
    >
      <header class="column-header">
        <span :class="`column-dot dot-${index % 5}`" />
        <h2>{{ column.name }}</h2>
        <span class="column-count">{{ (lists[column.id] ?? []).length }}</span>
        <Tooltip :label="t('Add ticket')">
          <ActionIcon
            variant="subtle" color="gray" :aria-label="`${t('Add ticket')}: ${column.name}`" size="sm"
            @click="onCreate(column.id)"
          >
            <Plus :size="16" />
          </ActionIcon>
        </Tooltip>
      </header>
      <div class="column-body">
        <draggable
          :list="lists[column.id] ?? []" group="tickets" item-key="id" handle=".drag-handle"
          draggable=".ticket" ghost-class="ticket-ghost" :animation="180" @start="onStart" @end="onEnd"
          @change="(event: any) => { onChange(column.id, event); }"
        >
          <template #item="{ element }">
            <TicketCard
              :ticket="element" :data="data" :grabbed="grabbedId === element.id"
              :on-open="onOpen" :on-grab="onGrab" :on-navigate="onNavigate"
            />
          </template>
          <template #footer>
            <div v-if="(lists[column.id] ?? []).length === 0" class="column-empty">{{ t('No tickets') }}</div>
            <button class="add-ticket" @click="onCreate(column.id)"><Plus :size="14" /> {{ t('Add ticket') }}</button>
          </template>
        </draggable>
      </div>
    </section>
  </div>
  <div v-if="dragged" class="drag-overlay" :style="{ left: `${overlayLeft}px`, top: `${pointer.y + 12}px`, width: draggedWidth ? `${draggedWidth}px` : undefined, position: 'fixed', zIndex: 1000, pointerEvents: 'none' }">
    <TicketCard :ticket="dragged" :data="data" :grabbed="false" :on-open="() => {}" :on-grab="() => {}" :on-navigate="() => {}" />
  </div>
  <div class="sr-only" role="status" aria-live="polite">{{ liveMessage }}</div>
</template>
