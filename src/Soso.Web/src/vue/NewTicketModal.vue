<script setup lang="ts">
import { onMounted, ref, useTemplateRef } from 'vue';
import { Button, Modal, Select, Stack, TextInput } from '@mantine-vue/core';
import type { Column } from '../api';
import { useLanguage } from './language';

const props = defineProps<{
    column: string;
    columns: Column[];
    onClose: () => void;
    onCreate: ( title: string, columnId: string ) => Promise<void>;
}>();
const { t } = useLanguage();
const title = ref( '' );
const selectedColumn = ref( props.column );
const busy = ref( false );
const titleInput = useTemplateRef<HTMLElement>( 'titleInput' );

onMounted( () => { titleInput.value?.querySelector( 'input' )?.focus(); } );

async function submit ()
{
    const current = title.value.trim();
    if ( busy.value || !current )
    {
        return;
    }
    busy.value = true;
    try
    {
        await props.onCreate( title.value, selectedColumn.value );
    }
    finally
    {
        busy.value = false;
    }
}
</script>

<template>
  <Modal :opened="true" :title="t('New ticket')" centered @close="onClose">
    <form @submit.prevent="submit">
      <Stack>
        <TextInput
          ref="titleInput" :label="t('Title')" :placeholder="t('What needs to happen?')" required :maxlength="160"
          v-model="title" data-autofocus
        />
        <Select
          :label="t('Column')" v-model="selectedColumn" :allow-deselect="false"
          :data="columns.map(column => ({ value: column.id, label: column.name }))"
        />
        <Button type="submit" :loading="busy" :disabled="!title.trim()">{{ t('Create ticket') }}</Button>
      </Stack>
    </form>
  </Modal>
</template>
