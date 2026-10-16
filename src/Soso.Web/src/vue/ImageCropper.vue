<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';

export type CropPoint = { x: number; y: number };
export type CropArea = { width: number; height: number; x: number; y: number };

// Faithful port of the react-easy-crop interaction subset the app uses:
// contain-fit media, mouse drag with position restriction, arrow-key nudging,
// and cropped-area computation. Class names are kept so styling and the e2e
// crop flow behave identically.
function clamp ( value: number, min: number, max: number ): number
{
    return Math.min( max, Math.max( min, value ) );
}

function limitArea ( max: number, value: number ): number
{
    return Math.min( max, Math.max( 0, value ) );
}

function getCropSize ( mediaWidth: number, mediaHeight: number, containerWidth: number, containerHeight: number, aspect: number ): { width: number; height: number }
{
    const fittingWidth = Math.min( mediaWidth, containerWidth );
    const fittingHeight = Math.min( mediaHeight, containerHeight );
    if ( fittingWidth > fittingHeight * aspect )
    {
        return { width: fittingHeight * aspect, height: fittingHeight };
    }
    return { width: fittingWidth, height: fittingWidth / aspect };
}

function restrictCoord ( position: number, mediaSize: number, cropSize: number, zoom: number ): number
{
    const maxPosition = Math.abs( ( mediaSize * zoom ) / 2 - cropSize / 2 );
    return clamp( position, -maxPosition, maxPosition );
}

function restrict ( position: CropPoint, mediaSize: { width: number; height: number }, cropSize: { width: number; height: number }, zoom: number ): CropPoint
{
    return {
        x: restrictCoord( position.x, mediaSize.width, cropSize.width, zoom ),
        y: restrictCoord( position.y, mediaSize.height, cropSize.height, zoom ),
    };
}

function computePixels (
    crop: CropPoint,
    media: { width: number; height: number; naturalWidth: number; naturalHeight: number },
    cropSize: { width: number; height: number },
    aspect: number,
    zoom: number,
): CropArea
{
    const percent = {
        x: limitArea( 100, ( ( ( media.width - cropSize.width / zoom ) / 2 - crop.x / zoom ) / media.width ) * 100 ),
        y: limitArea( 100, ( ( ( media.height - cropSize.height / zoom ) / 2 - crop.y / zoom ) / media.height ) * 100 ),
        width: limitArea( 100, ( ( cropSize.width / media.width ) * 100 ) / zoom ),
        height: limitArea( 100, ( ( cropSize.height / media.height ) * 100 ) / zoom ),
    };
    const widthInPixels = Math.round( limitArea( media.naturalWidth, ( percent.width * media.naturalWidth ) / 100 ) );
    const heightInPixels = Math.round( limitArea( media.naturalHeight, ( percent.height * media.naturalHeight ) / 100 ) );
    const wider = media.naturalWidth >= media.naturalHeight * aspect;
    const size = wider
        ? { width: Math.round( heightInPixels * aspect ), height: heightInPixels }
        : { width: widthInPixels, height: Math.round( widthInPixels / aspect ) };
    return {
        ...size,
        x: Math.round( limitArea( media.naturalWidth - size.width, ( percent.x * media.naturalWidth ) / 100 ) ),
        y: Math.round( limitArea( media.naturalHeight - size.height, ( percent.y * media.naturalHeight ) / 100 ) ),
    };
}

const props = defineProps<{
    image: string;
    crop: CropPoint;
    zoom: number;
    aspect?: number;
    disabled?: boolean;
}>();
const emit = defineEmits<{
    'update:crop': [ crop: CropPoint ];
    'crop-complete': [ area: CropArea ];
    error: [];
}>();

const aspect = props.aspect ?? 1;
const container = ref<HTMLDivElement | null>( null );
const media = ref<HTMLImageElement | null>( null );
const cropSize = ref<{ width: number; height: number } | null>( null );
const mediaBox = ref<{ width: number; height: number; naturalWidth: number; naturalHeight: number } | null>( null );
let dragging: { startX: number; startY: number; crop: CropPoint; } | null = null;
let raf = 0;

function measure ()
{
    const element = media.value;
    const box = container.value;
    if ( !element || !box || !element.naturalWidth )
    {
        return;
    }
    const rect = box.getBoundingClientRect();
    const mediaAspect = element.naturalWidth / element.naturalHeight;
    const rendered = rect.width / rect.height > mediaAspect
        ? { width: rect.height * mediaAspect, height: rect.height }
        : { width: rect.width, height: rect.width / mediaAspect };
    mediaBox.value = { ...rendered, naturalWidth: element.naturalWidth, naturalHeight: element.naturalHeight };
    cropSize.value = getCropSize( rendered.width, rendered.height, rect.width, rect.height, aspect );
    emitCrop();
}

function emitCrop ()
{
    if ( !mediaBox.value || !cropSize.value )
    {
        return;
    }
    const restricted = restrict( props.crop, mediaBox.value, cropSize.value, props.zoom );
    if ( restricted.x !== props.crop.x || restricted.y !== props.crop.y )
    {
        emit( 'update:crop', restricted );
    }
    emit( 'crop-complete', computePixels( restricted, mediaBox.value, cropSize.value, aspect, props.zoom ) );
}

function onMouseDown ( event: MouseEvent )
{
    if ( props.disabled )
    {
        return;
    }
    event.preventDefault();
    dragging = { startX: event.clientX, startY: event.clientY, crop: { ...props.crop } };
    document.addEventListener( 'mousemove', onMouseMove );
    document.addEventListener( 'mouseup', onMouseUp );
}

function onMouseMove ( event: MouseEvent )
{
    if ( !dragging )
    {
        return;
    }
    const start = dragging;
    cancelAnimationFrame( raf );
    raf = requestAnimationFrame( () =>
    {
        if ( !mediaBox.value || !cropSize.value )
        {
            return;
        }
        emit( 'update:crop', restrict(
            { x: start.crop.x + ( event.clientX - start.startX ), y: start.crop.y + ( event.clientY - start.startY ) },
            mediaBox.value,
            cropSize.value,
            props.zoom,
        ) );
    } );
}

function onMouseUp ()
{
    dragging = null;
    cancelAnimationFrame( raf );
    document.removeEventListener( 'mousemove', onMouseMove );
    document.removeEventListener( 'mouseup', onMouseUp );
    emitCrop();
}

function onKeyDown ( event: KeyboardEvent )
{
    if ( props.disabled || !mediaBox.value || !cropSize.value )
    {
        return;
    }
    const step = event.shiftKey ? 2 : 10;
    const next = { ...props.crop };
    if ( event.key === 'ArrowUp' )
    {
        next.y -= step;
    }
    else if ( event.key === 'ArrowDown' )
    {
        next.y += step;
    }
    else if ( event.key === 'ArrowLeft' )
    {
        next.x -= step;
    }
    else if ( event.key === 'ArrowRight' )
    {
        next.x += step;
    }
    else
    {
        return;
    }
    event.preventDefault();
    emit( 'update:crop', restrict( next, mediaBox.value, cropSize.value, props.zoom ) );
}

function onKeyUp ( event: KeyboardEvent )
{
    if ( [ 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight' ].includes( event.key ) )
    {
        event.preventDefault();
        emitCrop();
    }
}

watch( () => props.zoom, () => { emitCrop(); } );

let observer: ResizeObserver | null = null;
onMounted( () =>
{
    if ( container.value && typeof ResizeObserver !== 'undefined' )
    {
        observer = new ResizeObserver( () => { measure(); } );
        observer.observe( container.value );
    }
} );
onUnmounted( () =>
{
    observer?.disconnect();
    onMouseUp();
    cancelAnimationFrame( raf );
} );
</script>

<template>
  <div ref="container" class="reactEasyCrop_Container" tabindex="-1" @mousedown="onMouseDown" @keydown="onKeyDown" @keyup="onKeyUp">
    <img
      ref="media" :src="image" alt="" draggable="false" class="reactEasyCrop_Image reactEasyCrop_Contain"
      :style="{ transform: `translate(${crop.x}px, ${crop.y}px) scale(${zoom})` }"
      @load="measure()" @error="emit('error')"
    />
    <div
      v-if="cropSize" class="reactEasyCrop_CropArea reactEasyCrop_CropAreaRound"
      :style="{ width: `${cropSize.width}px`, height: `${cropSize.height}px` }"
    />
  </div>
</template>

<style scoped>
.reactEasyCrop_Container {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  overflow: hidden;
  user-select: none;
  touch-action: none;
  cursor: move;
  display: flex;
  justify-content: center;
  align-items: center;
}
.reactEasyCrop_Image {
  will-change: transform;
}
.reactEasyCrop_Contain {
  max-width: 100%;
  max-height: 100%;
  margin: auto;
  position: absolute;
  top: 0;
  bottom: 0;
  left: 0;
  right: 0;
}
.reactEasyCrop_CropArea {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  border: 1px solid rgba(255, 255, 255, 0.5);
  box-sizing: border-box;
  box-shadow: 0 0 0 9999em;
  color: rgba(0, 0, 0, 0.5);
  overflow: hidden;
}
.reactEasyCrop_CropAreaRound {
  border-radius: 50%;
}
</style>
