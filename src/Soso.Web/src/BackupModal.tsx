import { useEffect, useState } from 'react';
import { Button, Group, Modal, PasswordInput, Stack, Text, TextInput } from '@mantine/core';
import { Cloud, Save, Unplug } from 'lucide-react';
import { api } from './api';
import { reportError } from './feedback';
import { useLanguage } from './useLanguage';

type BackupStatus = { connected: boolean; appKey: string; lastBackupAt: string | null; lastError: string | null; };

export function BackupModal ( { onClose }: { onClose: () => void; } )
{
    const { t } = useLanguage();
    const [ status, setStatus ] = useState<BackupStatus | null>( null );
    const [ appKey, setAppKey ] = useState( '' );
    const [ accessToken, setAccessToken ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ message, setMessage ] = useState( '' );

    async function refresh ()
    {
        const result = await api<BackupStatus>( '/admin/backup' );
        setStatus( result );
        setAppKey( result.appKey );
    }

    useEffect( () =>
    {
        let active = true;
        api<BackupStatus>( '/admin/backup' ).then( result =>
        {
            if ( active )
            {
                setStatus( result ); setAppKey( result.appKey );
            }
        } ).catch( reportError );
        return () => { active = false; };
    }, [] );

    async function save ( event: React.FormEvent )
    {
        event.preventDefault(); setBusy( true ); setMessage( '' );
        try
        {
            const result = await api<BackupStatus>( '/admin/backup', 'PUT', { appKey, accessToken } );
            setStatus( result ); setAccessToken( '' ); setMessage( t( 'Dropbox connected successfully.' ) );
        }
        catch ( error ) { setMessage( error instanceof Error ? error.message : t( 'Dropbox connection failed.' ) ); reportError( error ); }
        finally { setBusy( false ); }
    }

    async function disconnect ()
    {
        setBusy( true );
        try
        {
            await api( '/admin/backup', 'DELETE' );
            setStatus( { connected: false, appKey: '', lastBackupAt: status?.lastBackupAt ?? null, lastError: null } );
            setAppKey( '' ); setAccessToken( '' ); setMessage( t( 'Dropbox disconnected.' ) );
        }
        catch ( error ) { setMessage( error instanceof Error ? error.message : t( 'Dropbox disconnection failed.' ) ); reportError( error ); }
        finally { setBusy( false ); }
    }

    async function runBackup ()
    {
        setBusy( true ); setMessage( '' );
        try
        {
            await api( '/admin/backup/run', 'POST' );
            await refresh(); setMessage( t( 'Backup completed.' ) );
        }
        catch ( error )
        {
            setMessage( error instanceof Error ? error.message : t( 'Backup failed. Check the Dropbox connection and try again.' ) );
            reportError( error );
            try { await refresh(); } catch ( refreshError ) { reportError( refreshError ); }
        }
        finally { setBusy( false ); }
    }

    return <Modal opened onClose={ onClose } title={ t( 'Backup' ) } size="md" centered>
        <form onSubmit={ event => { void save( event ); } }><Stack>
            <Text size="sm" c="dimmed">{ t( 'Connect a Dropbox app to store automatic database backups. Backups run 10 minutes after changes stop. If changes continue, a backup runs after 1 hour without a saved backup.' ) }</Text>
            <TextInput label={ t( 'Dropbox app key' ) } required maxLength={ 200 } value={ appKey } onChange={ event => { setAppKey( event.currentTarget.value ); } } />
            <PasswordInput label={ t( 'Dropbox access token' ) } required maxLength={ 4000 } autoComplete="new-password" placeholder={ status?.connected ? t( 'Enter a new token to replace the saved token' ) : '' } value={ accessToken } onChange={ event => { setAccessToken( event.currentTarget.value ); } } />
            { status && <Text size="sm">{ status.connected ? t( 'Dropbox connected' ) : t( 'Dropbox not connected' ) }{ status.lastBackupAt ? ` · ${ t( 'Last backup' ) }: ${ new Date( status.lastBackupAt ).toLocaleString() }` : '' }</Text> }
            { status?.lastError && <Text size="sm" c="red" role="alert">{ t( 'Last backup error' ) }: { status.lastError }</Text> }
            { message && <Text size="sm" c="teal" role="status">{ message }</Text> }
            <Group justify="space-between" wrap="wrap">
                <Group>
                    { status?.connected && <Button type="button" variant="light" color="red" leftSection={ <Unplug size={ 15 } /> } disabled={ busy } onClick={ () => { void disconnect(); } }>{ t( 'Disconnect' ) }</Button> }
                    { status?.connected && <Button type="button" variant="light" leftSection={ <Cloud size={ 15 } /> } loading={ busy } onClick={ () => { void runBackup(); } }>{ t( 'Back up now' ) }</Button> }
                </Group>
                <Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>{ status?.connected ? t( 'Save Dropbox settings' ) : t( 'Connect Dropbox' ) }</Button>
            </Group>
        </Stack></form>
    </Modal>;
}
