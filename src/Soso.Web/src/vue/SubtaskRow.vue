<script setup lang="ts">
import { ActionIcon, Checkbox, Group, TextInput, Tooltip } from '@mantine-vue/core';
import { GripVertical, Trash2 } from 'lucide-vue-next';
import type { Subtask } from '../api';
import { useLanguage } from './language';

export type SubtaskNavKey = 'up' | 'down' | 'drop' | 'cancel';

const props = defineProps<{
    task: Subtask;
    grabbed: boolean;
    onChange: ( task: Subtask ) => void;
    onRemove: ( id: string ) => void;
    onGrab: ( task: Subtask ) => void;
    onNavigate: ( task: Subtask, key: SubtaskNavKey ) => void;
}>();
const { t } = useLanguage();

function onHandleKey ( event: KeyboardEvent )
{
    if ( event.key === ' ' )
    {
        event.preventDefault();
        if ( props.grabbed )
        {
            props.onNavigate( props.task, 'drop' );
        }
        else
        {
            props.onGrab( props.task );
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
        props.onNavigate( props.task, 'up' );
    }
    else if ( event.key === 'ArrowDown' )
    {
        event.preventDefault();
        props.onNavigate( props.task, 'down' );
    }
    else if ( event.key === 'Escape' )
    {
        event.preventDefault();
        props.onNavigate( props.task, 'cancel' );
    }
}
</script>

<template>
  <Group gap="xs" wrap="nowrap" class="subtask-row" :data-subtask-id="task.id">
    <Tooltip :label="t('Move subtask')">
      <ActionIcon
        :aria-label="`${t('Move subtask')}: ${task.title}`" class="subtask-drag-handle" color="gray"
        variant="subtle" :aria-pressed="grabbed || undefined" @keydown="onHandleKey"
      >
        <GripVertical :size="15" />
      </ActionIcon>
    </Tooltip>
    <Checkbox
      :aria-label="`${t('Complete')} ${task.title}`" :checked="task.done"
      @update:checked="done => { onChange({ ...task, done: Boolean(done) }); }"
    />
    <TextInput
      :aria-label="t('Subtask title')" variant="unstyled" maxlength="300" required
      :model-value="task.title" :class="task.done ? 'completed-task' : ''" :style="{ flex: 1 }"
      @update:model-value="title => { onChange({ ...task, title: String(title ?? '') }); }"
    />
    <Tooltip :label="t('Remove subtask')">
      <ActionIcon :aria-label="t('Remove subtask')" color="gray" variant="subtle" @click="onRemove(task.id)">
        <Trash2 :size="14" />
      </ActionIcon>
    </Tooltip>
  </Group>
</template>
