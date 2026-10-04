import { useEffect, useRef, useState } from 'react';
import { ActionIcon, Avatar, Badge, Button, Checkbox, CopyButton, FileButton, Group, Modal, MultiSelect, PasswordInput, SegmentedControl, Stack, Tabs, Text, Textarea, TextInput, Tooltip, useMantineColorScheme } from '@mantine/core';
import { Check, Copy, ImagePlus, KeyRound, Plus, Save, Settings, Shield, Trash2, Unplug } from 'lucide-react';
import { api, imageUrl } from './api';
import type { Account, Board, Token } from './api';
import { reportError } from './feedback';
import { AvatarCropModal } from './AvatarCropModal';

function TokenBoardAccess ( { token, boards, busy, onSave, onRevoke }: { token: Token; boards: Board[]; busy: boolean; onSave: ( boardIds: string[] ) => void; onRevoke: () => void; } )
{
    const [ boardIds, setBoardIds ] = useState( token.boardIds );
    const unchanged = boardIds.length === token.boardIds.length && boardIds.every( id => token.boardIds.includes( id ) );
    const options = boards.map( board => ( { value: board.id, label: board.name } ) );
    const unavailable = token.boardIds.filter( id => !boards.some( board => board.id === id ) );
    return <Stack gap="xs" className="token-row">
        <Group justify="space-between" wrap="nowrap">
            <div style={ { minWidth: 0 } }><Text size="sm" fw={ 500 } style={ { overflowWrap: 'anywhere' } }>{ token.name }</Text><Text size="xs" c="dimmed">Expires { new Date( token.expiresAt ).toLocaleDateString() }</Text></div>
            <Tooltip label="Revoke token"><ActionIcon aria-label={ `Revoke ${ token.name }` } color="red" variant="subtle" disabled={ busy } onClick={ onRevoke }><Trash2 size={ 17 } /></ActionIcon></Tooltip>
        </Group>
        <MultiSelect label="Assigned boards" aria-label={ `Assigned boards for ${ token.name }` } placeholder="No boards selected" searchable clearable clearButtonProps={ { 'aria-label': `Clear boards for ${ token.name }`, 'aria-hidden': false, tabIndex: 0 } } disabled={ busy } value={ boardIds } onChange={ setBoardIds } data={ [ ...options, ...unavailable.map( id => ( { value: id, label: 'Unavailable board' } ) ) ] } />
        <Group justify="space-between">
            <Badge variant="light" color={ token.boardIds.length ? 'teal' : 'gray' }>{ token.boardIds.length ? `${ token.boardIds.length } assigned` : 'No board access' }</Badge>
            <Button size="xs" variant="light" leftSection={ <Save size={ 15 } /> } aria-label={ `Save boards for ${ token.name }` } disabled={ busy || unchanged } onClick={ () => { onSave( boardIds ); } }>Save boards</Button>
        </Group>
    </Stack>;
}

export function ProfileModal ( { account, onChange, onClose }: { account: Account; onChange: ( account: Account ) => void; onClose: () => void; } )
{
    const [ name, setName ] = useState( account.name );
    const [ theme, setTheme ] = useState( account.theme );
    const [ settings, setSettings ] = useState( account.settings ?? '' );
    const [ currentPassword, setCurrentPassword ] = useState( '' );
    const [ newPassword, setNewPassword ] = useState( '' );
    const [ tokens, setTokens ] = useState<Token[]>( [] );
    const [ boards, setBoards ] = useState<Board[]>( [] );
    const [ tokenName, setTokenName ] = useState( '' );
    const [ secret, setSecret ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ photo, setPhoto ] = useState<string | null>( null );
    const resetPhotoInput = useRef<() => void>( null );
    const { setColorScheme } = useMantineColorScheme();
    useEffect( () => { void api<Token[]>( '/auth/tokens' ).then( setTokens ).catch( reportError ); }, [] );
    useEffect( () => { void api<Board[]>( '/boards' ).then( setBoards ).catch( reportError ); }, [] );
    useEffect( () => () =>
    {
        if ( photo )
        {
            URL.revokeObjectURL( photo );
        }
    }, [ photo ] );
    async function action ( work: () => Promise<void> )
    {
        setBusy( true );
        try
        {
            await work();
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
    return <><Modal opened onClose={ onClose } title="Profile & settings" size="lg" centered><Tabs defaultValue="profile"><Tabs.List mb="lg"><Tabs.Tab value="profile" leftSection={ <Settings size={ 15 } /> }>Profile</Tabs.Tab><Tabs.Tab value="security" leftSection={ <Shield size={ 15 } /> }>Security</Tabs.Tab><Tabs.Tab value="mcp" leftSection={ <Unplug size={ 15 } /> }>MCP</Tabs.Tab></Tabs.List>
        <Tabs.Panel value="profile"><form onSubmit={ event => { event.preventDefault(); void action( async () => { const updated = await api<Account>( '/auth/profile', 'PUT', { name, theme, settings } ); onChange( updated ); setColorScheme( theme ); onClose(); } ); } }><Stack><Group><Avatar size={ 64 } radius="xl" src={ imageUrl( account.avatarId ) }>{ account.name.slice( 0, 2 ) }</Avatar><FileButton resetRef={ resetPhotoInput } accept="image/png,image/jpeg,image/webp" onChange={ file => { setPhoto( file ? URL.createObjectURL( file ) : null ); resetPhotoInput.current?.(); } }>{ props => <Button { ...props } variant="light" size="xs" disabled={ busy } leftSection={ <ImagePlus size={ 15 } /> }>Change photo</Button> }</FileButton></Group><TextInput label="Name" value={ name } required maxLength={ 80 } onChange={ event => { setName( event.currentTarget.value ); } } /><TextInput label="Email" value={ account.email } readOnly /><Text size="sm" fw={ 500 }>Appearance</Text><SegmentedControl value={ theme } onChange={ value => { setTheme( value as 'light' | 'dark' ); } } data={ [ { label: 'Light', value: 'light' }, { label: 'Dark', value: 'dark' } ] } /><Textarea label="Custom settings" value={ settings } maxLength={ 4000 } minRows={ 4 } onChange={ event => { setSettings( event.currentTarget.value ); } } /><Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>Save profile</Button></Stack></form></Tabs.Panel>
        <Tabs.Panel value="security"><form onSubmit={ event => { event.preventDefault(); void action( async () => { await api( '/auth/password', 'PUT', { currentPassword, newPassword } ); window.dispatchEvent( new Event( 'soso-session-expired' ) ); onClose(); } ); } }><Stack><PasswordInput label="Current password" autoComplete="current-password" required maxLength={ 128 } value={ currentPassword } onChange={ event => { setCurrentPassword( event.currentTarget.value ); } } /><PasswordInput label="New password" autoComplete="new-password" description="14 characters minimum" required minLength={ 14 } maxLength={ 128 } value={ newPassword } onChange={ event => { setNewPassword( event.currentTarget.value ); } } /><Text size="xs" c="dimmed">Changing your password ends all sessions and revokes all MCP tokens.</Text><Button type="submit" loading={ busy } leftSection={ <KeyRound size={ 15 } /> }>Change password</Button></Stack></form></Tabs.Panel>
        <Tabs.Panel value="mcp"><Stack>
            <TextInput label="MCP endpoint" readOnly value={ `${ window.location.origin }/mcp` } />
            <form onSubmit={ event => { event.preventDefault(); void action( async () => { const result = await api<{ token: string; }>( '/auth/tokens', 'POST', { name: tokenName } ); setSecret( result.token ); setTokenName( '' ); setTokens( await api<Token[]>( '/auth/tokens' ) ); } ); } }><Group align="end"><TextInput label="Token name" required maxLength={ 80 } value={ tokenName } onChange={ event => { setTokenName( event.currentTarget.value ); } } style={ { flex: 1, minWidth: 140 } } /><Button type="submit" loading={ busy } leftSection={ <Plus size={ 15 } /> }>Create token</Button></Group></form>
            { secret && <Stack gap="xs"><Text size="xs" c="dimmed">Copy this token now. It will not be shown again. Send it as Authorization: Bearer &lt;token&gt;.</Text><TextInput label="New token" readOnly value={ secret } rightSection={ <CopyButton value={ secret }>{ ( { copied, copy } ) => <Tooltip label={ copied ? 'Copied' : 'Copy token' }><ActionIcon aria-label="Copy token" variant="subtle" onClick={ copy }>{ copied ? <Check size={ 16 } /> : <Copy size={ 16 } /> }</ActionIcon></Tooltip> }</CopyButton> } /></Stack> }
            { tokens.map( token => <TokenBoardAccess key={ token.id } token={ token } boards={ boards } busy={ busy } onSave={ boardIds => { void action( async () => { const updated = await api<Token>( `/auth/tokens/${ token.id }/boards`, 'PUT', { boardIds } ); setTokens( tokens.map( item => item.id === updated.id ? updated : item ) ); } ); } } onRevoke={ () => { void action( async () => { await api( `/auth/tokens/${ token.id }`, 'DELETE' ); setTokens( tokens.filter( item => item.id !== token.id ) ); setSecret( '' ); } ); } } /> ) }
        </Stack></Tabs.Panel>
    </Tabs></Modal>{ photo && <AvatarCropModal source={ photo } onClose={ () => { setPhoto( null ); } } onSave={ async cropped => { const form = new FormData(); form.append( 'file', cropped ); onChange( await api<Account>( '/auth/avatar', 'POST', form ) ); setPhoto( null ); } } /> }</>;
}

export function AdminModal ( { onClose }: { onClose: () => void; } )
{
    const [ accounts, setAccounts ] = useState<Account[]>( [] );
    const [ name, setName ] = useState( '' );
    const [ email, setEmail ] = useState( '' );
    const [ password, setPassword ] = useState( '' );
    const [ isAdmin, setIsAdmin ] = useState( false );
    const [ busy, setBusy ] = useState( false );
    const [ selected, setSelected ] = useState<Account | null>( null );
    const [ disabled, setDisabled ] = useState( false );
    const [ resetPassword, setResetPassword ] = useState( '' );
    useEffect( () => { void api<Account[]>( '/admin/accounts' ).then( setAccounts ).catch( reportError ); }, [] );
    async function create ( event: React.FormEvent )
    {
        event.preventDefault();
        setBusy( true );
        try
        {
            const result = await api<Account>( '/admin/accounts', 'POST', { name, email, password, isAdmin } );
            setAccounts( [ ...accounts, result ] );
            setName( '' ); setEmail( '' ); setPassword( '' ); setIsAdmin( false );
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
    async function update ( event: React.FormEvent )
    {
        event.preventDefault();
        if ( !selected )
        {
            return;
        }
        setBusy( true );
        try
        {
            const updated = await api<Account>( `/admin/accounts/${ selected.id }`, 'PUT', { disabled, password: resetPassword || null } );
            setAccounts( accounts.map( account => account.id === updated.id ? updated : account ) );
            setSelected( null );
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
    return <Modal opened onClose={ onClose } title="Account management" size="lg" centered><Tabs defaultValue="accounts"><Tabs.List mb="lg"><Tabs.Tab value="accounts">Accounts</Tabs.Tab><Tabs.Tab value="create" leftSection={ <Plus size={ 15 } /> }>New account</Tabs.Tab></Tabs.List><Tabs.Panel value="accounts"><Stack gap={ 0 }>{ accounts.map( account => <Group key={ account.id } justify="space-between" className="account-row" wrap="nowrap"><Group gap="sm" wrap="nowrap" style={ { minWidth: 0 } }><Avatar src={ imageUrl( account.avatarId ) } radius="xl" size={ 32 }>{ account.name.slice( 0, 1 ) }</Avatar><div className="account-info"><Text size="sm" fw={ 500 } truncate>{ account.name }</Text><Text size="xs" c="dimmed" truncate>{ account.email }</Text></div></Group><Group gap="xs" wrap="nowrap"><Badge size="xs" color={ account.disabled ? 'red' : account.isAdmin ? 'teal' : 'gray' } variant="light">{ account.disabled ? 'Disabled' : account.isAdmin ? 'Admin' : 'Member' }</Badge><Tooltip label="Manage account"><ActionIcon aria-label={ `Manage ${ account.name }` } variant="subtle" onClick={ () => { setSelected( account ); setDisabled( account.disabled ); setResetPassword( '' ); } }><Settings size={ 16 } /></ActionIcon></Tooltip></Group></Group> ) }</Stack></Tabs.Panel><Tabs.Panel value="create"><form onSubmit={ event => { void create( event ); } }><Stack><TextInput label="Name" required maxLength={ 80 } value={ name } onChange={ event => { setName( event.currentTarget.value ); } } /><TextInput label="Email" type="email" required maxLength={ 254 } value={ email } onChange={ event => { setEmail( event.currentTarget.value ); } } /><PasswordInput label="Initial password" autoComplete="new-password" description="14 characters minimum" required minLength={ 14 } maxLength={ 128 } value={ password } onChange={ event => { setPassword( event.currentTarget.value ); } } /><Checkbox label="System administrator" checked={ isAdmin } onChange={ event => { setIsAdmin( event.currentTarget.checked ); } } /><Button type="submit" loading={ busy } leftSection={ <Plus size={ 15 } /> }>Create account</Button></Stack></form></Tabs.Panel></Tabs><Modal opened={ selected !== null } onClose={ () => { setSelected( null ); } } title={ `Manage ${ selected?.name ?? '' }` } centered><form onSubmit={ event => { void update( event ); } }><Stack><Checkbox label="Disable account" checked={ disabled } onChange={ event => { setDisabled( event.currentTarget.checked ); } } /><PasswordInput label="Reset password" autoComplete="new-password" description="Leave empty to keep the existing password" minLength={ 14 } maxLength={ 128 } value={ resetPassword } onChange={ event => { setResetPassword( event.currentTarget.value ); } } /><Text size="xs" c="dimmed">Saving ends this account's sessions and revokes its MCP tokens.</Text><Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>Save account</Button></Stack></form></Modal></Modal>;
}