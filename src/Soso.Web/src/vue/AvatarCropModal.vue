<script setup lang="ts">
import { ref } from 'vue';
import { Button, Group, Modal, Slider, Stack, Text } from '@mantine-vue/core';
import { RotateCcw, Save } from 'lucide-vue-next';
import ImageCropper, { type CropArea } from './ImageCropper.vue';
import { reportError } from './feedback';

const props = defineProps<{
    source: string;
    onSave: ( photo: File ) => Promise<void>;
    onClose: () => void;
}>();
const crop = ref( { x: 0, y: 0 } );
const zoom = ref( 1 );
const area = ref<CropArea | null>( null );
const busy = ref( false );
const failed = ref( false );

async function cropPhoto ( source: string, selected: CropArea ): Promise<File>
{
    const image = new Image();
    image.src = source;
    await image.decode();
    const canvas = document.createElement( 'canvas' );
    canvas.width = 512;
    canvas.height = 512;
    const context = canvas.getContext( '2d' );
    if ( !context )
    {
        throw new Error( 'Unable to prepare the photo.' );
    }
    context.drawImage( image, selected.x, selected.y, selected.width, selected.height, 0, 0, 512, 512 );
    const blob = await new Promise<Blob>( ( resolve, reject ) =>
    {
        canvas.toBlob( result =>
        {
            if ( !result )
            {
                reject( new Error( 'Unable to crop the photo.' ) );
                return;
            }
            resolve( result );
        }, 'image/png' );
    } );
    return new File( [ blob ], 'avatar.png', { type: 'image/png' } );
}

async function save ()
{
    if ( !area.value )
    {
        return;
    }
    busy.value = true;
    try
    {
        await props.onSave( await cropPhoto( props.source, area.value ) );
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

function onSliderKey ( event: KeyboardEvent )
{
    if ( event.key === 'End' )
    {
        event.preventDefault();
        zoom.value = 3;
    }
    else if ( event.key === 'Home' )
    {
        event.preventDefault();
        zoom.value = 1;
    }
}
</script>

<template>
  <Modal :opened="true" title="Edit photo" size="md" centered :close-on-click-outside="!busy" :close-on-escape="!busy" :with-close-button="!busy" @close="onClose">
    <Stack>
      <div :style="{ position: 'relative', width: '100%', aspectRatio: '1', overflow: 'hidden', borderRadius: 4, background: '#171717' }">
        <ImageCropper
          v-if="source && !failed" :image="source" v-model:crop="crop" :zoom="zoom" :aspect="1" :disabled="busy"
          @crop-complete="value => { area = value; }" @error="failed = true; area = null;"
        />
        <Text v-if="failed" c="red" p="md" role="alert">Unable to open this photo. Choose another image.</Text>
      </div>
      <div>
        <Text size="sm" :fw="500" mb="xs">Zoom</Text>
        <Slider thumb-label="Photo zoom" :min="1" :max="3" :step="0.01" v-model="zoom" :disabled="busy || failed" @keydown="onSliderKey" />
      </div>
      <Group justify="space-between">
        <Button variant="subtle" :disabled="busy || failed" @click="crop = { x: 0, y: 0 }; zoom = 1;">
          <template #leftSection><RotateCcw :size="16" /></template>
          Reset
        </Button>
        <Group gap="xs">
          <Button variant="default" :disabled="busy" @click="onClose()">Cancel</Button>
          <Button :loading="busy" :disabled="!area || failed" @click="save()">
            <template #leftSection><Save :size="16" /></template>
            Save photo
          </Button>
        </Group>
      </Group>
    </Stack>
  </Modal>
</template>
