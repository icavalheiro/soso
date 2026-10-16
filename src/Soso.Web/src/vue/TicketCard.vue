<script setup lang="ts">
import { computed } from 'vue';
import { Avatar, Badge, Group, Progress, Tooltip } from '@mantine-vue/core';
import { Check, CheckSquare, MessageSquare, CalendarDays, GripVertical } from 'lucide-vue-next';
import { imageUrl, tags, ticketKey } from '../api';
import type { BoardData, Ticket } from '../api';
import MarkdownText from './MarkdownText.vue';
import { useLanguage } from './language';

export type CardNavKey = 'up' | 'down' | 'left' | 'right' | 'drop' | 'cancel';

const props = defineProps<{
    ticket: Ticket;
    data: BoardData;
    grabbed: boolean;
    onOpen: ( ticket: Ticket ) => void;
    onGrab: ( ticket: Ticket ) => void;
    onNavigate: ( ticket: Ticket, key: CardNavKey ) => void;
}>();
const { t } = useLanguage();
const now = Date.now();
const done = computed( () => props.ticket.subtasks.filter( task => task.done ).length );
const assignee = computed( () => props.data.members.find( member => member.id === props.ticket.assigneeId ) );
const color = computed( () => ( { low: 'gray', normal: 'teal', high: 'yellow', urgent: 'red' } as Record<string, string> )[ props.ticket.priority ] );
const overdue = computed( () => props.ticket.dueDate !== null && new Date( props.ticket.dueDate ).getTime() < now );
const completed = computed( () => props.data.board.columns.find( column => column.id === props.ticket.columnId )?.isDone );

function onHandleKey ( event: KeyboardEvent )
{
    if ( event.key === ' ' )
    {
        event.preventDefault();
        if ( props.grabbed )
        {
            props.onNavigate( props.ticket, 'drop' );
        }
        else
        {
            props.onGrab( props.ticket );
        }
        return;
    }
    if ( !props.grabbed )
    {
        return;
    }
    if ( event.key === 'ArrowUp' )
    {
        event.preventDefault();
        props.onNavigate( props.ticket, 'up' );
    }
    else if ( event.key === 'ArrowDown' )
    {
        event.preventDefault();
        props.onNavigate( props.ticket, 'down' );
    }
    else if ( event.key === 'ArrowLeft' )
    {
        event.preventDefault();
        props.onNavigate( props.ticket, 'left' );
    }
    else if ( event.key === 'ArrowRight' )
    {
        event.preventDefault();
        props.onNavigate( props.ticket, 'right' );
    }
    else if ( event.key === 'Escape' )
    {
        event.preventDefault();
        props.onNavigate( props.ticket, 'cancel' );
    }
}
</script>

<template>
  <article :class="`ticket priority-${ticket.priority}`" :data-ticket-id="ticket.id">
    <button class="ticket-open" :aria-label="`${t('Open ticket')}: ${ticket.title}`" @click="onOpen(ticket)">
      <img v-if="ticket.images[0]" class="ticket-cover" :src="imageUrl(ticket.images[0])" :alt="ticket.title" loading="lazy" />
      <div class="ticket-content">
        <div class="ticket-meta">
          <Group gap="4px">
            <span v-for="value in ticket.tags" :key="value" class="tag-chip">
              <i :style="{ background: tags.find(tag => tag.value === value)?.color }" />{{ tags.find(tag => tag.value === value)?.label ?? value }}
            </span>
          </Group>
          <span v-if="completed" class="done-check"><Check :size="12" /></span>
          <Badge v-else-if="ticket.priority !== 'normal'" size="xs" variant="light" :color="color">{{ ticket.priority }}</Badge>
        </div>
        <h3 :class="completed ? 'completed-title' : ''">{{ ticket.title }}</h3>
        <MarkdownText v-if="ticket.description" class="ticket-excerpt" :source="ticket.description" mode="excerpt" />
        <Progress v-if="ticket.subtasks.length > 0" :value="done / ticket.subtasks.length * 100" :color="done === ticket.subtasks.length ? 'teal' : 'yellow'" size="3px" mt="12px" mb="8px" />
        <div class="ticket-footer">
          <Group gap="9px">
            <span class="ticket-id">{{ ticketKey(data.board, ticket) }}</span>
            <span v-if="ticket.subtasks.length > 0" class="ticket-type"><CheckSquare :size="12" />{{ done }}/{{ ticket.subtasks.length }}</span>
            <span v-if="ticket.comments.length > 0" class="ticket-type"><MessageSquare :size="12" />{{ ticket.comments.length }}</span>
            <span v-if="ticket.dueDate" :class="`ticket-type ${overdue ? 'overdue' : ''}`"><CalendarDays :size="12" />{{ new Date(ticket.dueDate).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) }}</span>
          </Group>
          <span v-if="assignee" class="ticket-assignee">
            <Avatar :size="20" radius="xl" :src="imageUrl(assignee.avatarId)">{{ assignee.name.slice(0, 1) }}</Avatar>{{ assignee.name.split(' ')[0] }}
          </span>
        </div>
      </div>
    </button>
    <Tooltip :label="t('Move ticket')">
      <button
        class="drag-handle" :aria-label="`${t('Move ticket')}: ${ticket.title}`" :aria-pressed="grabbed || undefined"
        @keydown="onHandleKey"
      >
        <GripVertical :size="14" />
      </button>
    </Tooltip>
  </article>
</template>
