import { useEffect, useState } from 'react';
import { ActionIcon, Alert, Button, CopyButton, Group, Modal, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { AlertCircle, Check, Cloud, Copy, Unplug } from 'lucide-react';
import { api } from './api';
import { reportError } from './feedback';
import { useLanguage } from './useLanguage';

type BackupStatus = { connected: boolean; appKey: string; redirectUri: string; lastBackupAt: string | null; lastError: string | null; };

function DropboxMark ( { size = 22 }: { size?: number; } )
{
    return <svg width={ size } height={ size } viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 1.807L0 5.629l6 3.822 6.001-3.822L6 1.807zM18 1.807l-6 3.822 6 3.822 6-3.822-6-3.822zM0 13.274l6 3.822 6.001-3.822L6 9.452l-6 3.822zM18 9.452l-6 3.822 6 3.822 6-3.822-6-3.822zM6 18.371l6.001 3.822 6-3.822-6-3.822L6 18.371z" /></svg>;
}

export function BackupModal ( { result, onClose }: { result: string | null; onClose: () => void; } )
{
    const { t } = useLanguage();
    const [ status, setStatus ] = useState<BackupStatus | null>( null );
    const [ appKey, setAppKey ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ failed, setFailed ] = useState( () => result === 'cancelled' || result === 'error' );
    const [ message, setMessage ] = useState( () =>
    {
        if ( result === 'connected' )
        {
            return t( 'Dropbox connected successfully.' );
        }
        if ( result === 'cancelled' )
        {
            return t( 'Dropbox authorization was cancelled.' );
        }
        return result === 'error' ? t( 'Dropbox connection failed.' ) : '';
    } );
    const missingWriteScope = [ message, status?.lastError ?? '' ].some( value => value.includes( 'files.content.write' ) || value.includes( 'missing_scope' ) );

    async function refresh ()
    {
        const next = await api<BackupStatus>( '/admin/backup' );
        setStatus( next );
        setAppKey( next.appKey );
    }

    useEffect( () =>
    {
        let active = true;
        api<BackupStatus>( '/admin/backup' ).then( next =>
        {
            if ( active )
            {
                setStatus( next ); setAppKey( next.appKey );
            }
        } ).catch( reportError );
        return () => { active = false; };
    }, [] );

    async function connect ()
    {
        setBusy( true ); setMessage( '' ); setFailed( false );
        try
        {
            const key = appKey.trim();
            if ( !key )
            {
                throw new Error( t( 'Enter the Dropbox app key before connecting.' ) );
            }
            if ( status?.appKey !== key )
            {
                await api( '/admin/backup', 'PUT', { appKey: key } );
            }
            const authorization = await api<{ url: string; }>( '/admin/backup/dropbox/authorize' );
            window.location.assign( authorization.url );
        }
        catch ( error )
        {
            setFailed( true );
            setMessage( error instanceof Error ? error.message : t( 'Dropbox connection failed.' ) );
            reportError( error );
            setBusy( false );
        }
    }

    async function disconnect ()
    {
        setBusy( true ); setMessage( '' ); setFailed( false );
        try
        {
            await api( '/admin/backup', 'DELETE' );
            await refresh(); setMessage( t( 'Dropbox disconnected.' ) );
        }
        catch ( error )
        {
            setFailed( true );
            setMessage( error instanceof Error ? error.message : t( 'Dropbox disconnection failed.' ) );
            reportError( error );
        }
        finally { setBusy( false ); }
    }

    async function runBackup ()
    {
        setBusy( true ); setMessage( '' ); setFailed( false );
        try
        {
            await api( '/admin/backup/run', 'POST' );
            await refresh(); setMessage( t( 'Backup completed.' ) );
        }
        catch ( error )
        {
            setFailed( true );
            setMessage( error instanceof Error ? error.message : t( 'Backup failed. Check the Dropbox connection and try again.' ) );
            reportError( error );
            try { await refresh(); } catch ( refreshError ) { reportError( refreshError ); }
        }
        finally { setBusy( false ); }
    }

    return <Modal opened onClose={ onClose } title={ t( 'Backup' ) } size="md" centered><Stack>
        <div className="dropbox-panel">
            <span className="dropbox-mark"><DropboxMark /></span>
            <div>
                <Text fw={ 600 }>Dropbox</Text>
                <Text size="sm" c="dimmed">{ status?.connected ? t( 'Dropbox connected' ) : t( 'Dropbox not connected' ) }</Text>
            </div>
        </div>
        <Text size="sm" c="dimmed">{ t( 'Connect a Dropbox app to store automatic database backups. Backups run 10 minutes after changes stop. If changes continue, a backup runs after 1 hour without a saved backup.' ) }</Text>
        <Text size="sm" c="dimmed">{ t( 'Create an app in the Dropbox App Console, add the redirect URI below under OAuth redirect URIs, and enable files.content.write.' ) }</Text>
        { missingWriteScope ?
            <Alert color="red" icon={ <AlertCircle size={ 18 } /> } title={ t( 'Dropbox write permission required' ) } role="alert">
                <Text size="sm">{ t( 'Dropbox rejected this connection because the app does not include files.content.write. In the Dropbox App Console, enable files.content.write, then reconnect Dropbox.' ) }</Text>
            </Alert> :
            <Text size="sm" c="dimmed">{ t( 'The Dropbox app needs the files.content.write permission. Enable it in the Dropbox App Console before connecting, or reconnect after changing it.' ) }</Text> }
        <TextInput label={ t( 'Dropbox app key' ) } required maxLength={ 200 } value={ appKey } onChange={ event => { setAppKey( event.currentTarget.value ); } } />
        { status && <TextInput label={ t( 'Dropbox redirect URI' ) } readOnly value={ status.redirectUri } rightSection={ <CopyButton value={ status.redirectUri } timeout={ 1500 }>{ ( { copied, copy } ) => <Tooltip label={ copied ? t( 'Copied' ) : t( 'Copy redirect URI' ) }><ActionIcon aria-label={ t( 'Copy redirect URI' ) } variant="subtle" color="gray" onClick={ copy }>{ copied ? <Check size={ 15 } /> : <Copy size={ 15 } /> }</ActionIcon></Tooltip> }</CopyButton> } /> }
        { status?.lastBackupAt && <Text size="sm">{ t( 'Last backup' ) }: { new Date( status.lastBackupAt ).toLocaleString() }</Text> }
        { status?.lastError && !missingWriteScope && <Text size="sm" c="red" role="alert">{ t( 'Last backup error' ) }: { status.lastError }</Text> }
        { message && <Text size="sm" c={ failed ? 'red' : 'teal' } role={ failed ? 'alert' : 'status' }>{ message }</Text> }
        <Group justify="space-between" wrap="wrap">
            <Group>
                { status?.connected && <Button type="button" variant="light" color="red" leftSection={ <Unplug size={ 15 } /> } disabled={ busy } onClick={ () => { void disconnect(); } }>{ t( 'Disconnect' ) }</Button> }
                { status?.connected && <Button type="button" variant="light" leftSection={ <Cloud size={ 15 } /> } loading={ busy } onClick={ () => { void runBackup(); } }>{ t( 'Back up now' ) }</Button> }
            </Group>
            <Button type="button" className="dropbox-connect" leftSection={ <DropboxMark size={ 16 } /> } loading={ busy } onClick={ () => { void connect(); } }>{ status?.connected ? t( 'Reconnect Dropbox' ) : t( 'Connect Dropbox' ) }</Button>
        </Group>
    </Stack></Modal>;
}