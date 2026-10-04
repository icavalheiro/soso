import { useState } from 'react';
import { Button, Group, Modal, Slider, Stack, Text } from '@mantine/core';
import { RotateCcw, Save } from 'lucide-react';
import Cropper from 'react-easy-crop';
import type { Area } from 'react-easy-crop';
import { reportError } from './feedback';

async function cropPhoto ( source: string, area: Area ): Promise<File>
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
    context.drawImage( image, area.x, area.y, area.width, area.height, 0, 0, 512, 512 );
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

export function AvatarCropModal ( { source, onSave, onClose }: { source: string; onSave: ( photo: File ) => Promise<void>; onClose: () => void; } )
{
    const [ crop, setCrop ] = useState( { x: 0, y: 0 } );
    const [ zoom, setZoom ] = useState( 1 );
    const [ area, setArea ] = useState<Area | null>( null );
    const [ busy, setBusy ] = useState( false );
    const [ failed, setFailed ] = useState( false );
    async function save ()
    {
        if ( !area )
        {
            return;
        }
        setBusy( true );
        try
        {
            await onSave( await cropPhoto( source, area ) );
        }
        catch ( error )
        {
            reportError( error );
        }
        finally
        {
            setBusy( false );
        }
    }
    return <Modal opened onClose={ onClose } title="Edit photo" size="md" centered closeOnClickOutside={ !busy } closeOnEscape={ !busy } withCloseButton={ !busy }>
        <Stack>
            <div style={ { position: 'relative', width: '100%', aspectRatio: '1', overflow: 'hidden', borderRadius: 4, background: '#171717' } }>
                { source && !failed && <Cropper image={ source } crop={ crop } zoom={ zoom } aspect={ 1 } cropShape="round" onCropChange={ setCrop } onZoomChange={ setZoom } onCropComplete={ ( _, pixels ) => { setArea( pixels ); } } keyboardStep={ 10 } mediaProps={ { onError: () => { setFailed( true ); setArea( null ); } } } style={ { containerStyle: { pointerEvents: busy ? 'none' : 'auto' } } } /> }
                { failed && <Text c="red" p="md" role="alert">Unable to open this photo. Choose another image.</Text> }
            </div>
            <div><Text size="sm" fw={ 500 } mb="xs">Zoom</Text><Slider thumbLabel="Photo zoom" min={ 1 } max={ 3 } step={ 0.01 } value={ zoom } onChange={ setZoom } disabled={ busy || failed } /></div>
            <Group justify="space-between">
                <Button variant="subtle" disabled={ busy || failed } leftSection={ <RotateCcw size={ 16 } /> } onClick={ () => { setCrop( { x: 0, y: 0 } ); setZoom( 1 ); } }>Reset</Button>
                <Group gap="xs"><Button variant="default" disabled={ busy } onClick={ onClose }>Cancel</Button><Button leftSection={ <Save size={ 16 } /> } loading={ busy } disabled={ !area || failed } onClick={ () => { void save(); } }>Save photo</Button></Group>
            </Group>
        </Stack>
    </Modal>;
}