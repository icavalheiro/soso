import { useEffect, useState } from 'react';
import { ActionIcon, Avatar, Badge, Button, Checkbox, Group, Modal, MultiSelect, PasswordInput, Stack, Tabs, Text, TextInput, Tooltip } from '@mantine/core';
import { Plus, Settings } from 'lucide-react';
import { api, imageUrl } from './api';
import type { Account, Board } from './api';
import { reportError } from './feedback';
import { useLanguage } from './useLanguage';

export function AdminModal ( { onClose }: { onClose: () => void; } )
{
    const { t } = useLanguage();
    const [ accounts, setAccounts ] = useState<Account[]>( [] );
    const [ boards, setBoards ] = useState<Board[]>( [] );
    const [ name, setName ] = useState( '' );
    const [ email, setEmail ] = useState( '' );
    const [ password, setPassword ] = useState( '' );
    const [ isAdmin, setIsAdmin ] = useState( false );
    const [ busy, setBusy ] = useState( false );
    const [ selected, setSelected ] = useState<Account | null>( null );
    const [ disabled, setDisabled ] = useState( false );
    const [ boardIds, setBoardIds ] = useState<string[]>( [] );
    const [ resetPassword, setResetPassword ] = useState( '' );
    useEffect( () =>
    {
        void api<Account[]>( '/admin/accounts' ).then( setAccounts ).catch( reportError );
        void api<Board[]>( '/boards' ).then( setBoards ).catch( reportError );
    }, [] );

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
            const updated = await api<Account>( `/admin/accounts/${ selected.id }`, 'PUT', { disabled, password: resetPassword || null, boardIds } );
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

    function editAccount ( account: Account )
    {
        setSelected( account );
        setDisabled( account.disabled );
        setBoardIds( account.boardIds ?? boards.filter( board => board.ownerId === account.id || board.members.includes( account.id ) ).map( board => board.id ) );
        setResetPassword( '' );
    }

    return <Modal opened onClose={ onClose } title={ t( 'Account management' ) } size="lg" centered>
        <Tabs defaultValue="accounts">
            <Tabs.List mb="lg"><Tabs.Tab value="accounts">{ t( 'Accounts' ) }</Tabs.Tab><Tabs.Tab value="create" leftSection={ <Plus size={ 15 } /> }>{ t( 'New account' ) }</Tabs.Tab></Tabs.List>
            <Tabs.Panel value="accounts"><Stack gap={ 0 }>{ accounts.map( account => <Group key={ account.id } justify="space-between" className="account-row" wrap="nowrap">
                <Group gap="sm" wrap="nowrap" style={ { minWidth: 0 } }><Avatar src={ imageUrl( account.avatarId ) } radius="xl" size={ 32 }>{ account.name.slice( 0, 1 ) }</Avatar><div className="account-info"><Text size="sm" fw={ 500 } truncate>{ account.name }</Text><Text size="xs" c="dimmed" truncate>{ account.email }</Text></div></Group>
                <Group gap="xs" wrap="nowrap"><Badge size="xs" color={ account.disabled ? 'red' : account.isAdmin ? 'teal' : 'gray' } variant="light">{ account.disabled ? t( 'Disabled' ) : account.isAdmin ? t( 'Admin' ) : t( 'Member' ) }</Badge><Tooltip label={ t( 'Manage account' ) }><ActionIcon aria-label={ `${ t( 'Manage account' )}: ${ account.name }` } variant="subtle" onClick={ () => { editAccount( account ); } }><Settings size={ 16 } /></ActionIcon></Tooltip></Group>
            </Group> ) }</Stack></Tabs.Panel>
            <Tabs.Panel value="create"><form onSubmit={ event => { void create( event ); } }><Stack>
                <TextInput label={ t( 'Name' ) } required maxLength={ 80 } value={ name } onChange={ event => { setName( event.currentTarget.value ); } } />
                <TextInput label={ t( 'Email' ) } type="email" required maxLength={ 254 } value={ email } onChange={ event => { setEmail( event.currentTarget.value ); } } />
                <PasswordInput label={ t( 'Initial password' ) } autoComplete="new-password" description={ t( '14 characters minimum' ) } required minLength={ 14 } maxLength={ 128 } value={ password } onChange={ event => { setPassword( event.currentTarget.value ); } } />
                <Checkbox label={ t( 'System administrator' ) } checked={ isAdmin } onChange={ event => { setIsAdmin( event.currentTarget.checked ); } } />
                <Button type="submit" loading={ busy } leftSection={ <Plus size={ 15 } /> }>{ t( 'Create account' ) }</Button>
            </Stack></form></Tabs.Panel>
        </Tabs>
        { selected && <Modal opened onClose={ () => { setSelected( null ); } } title={ `${ t( 'Manage account' )}: ${ selected.name }` } centered>
            <form onSubmit={ event => { void update( event ); } }><Stack>
                <MultiSelect label={ t( 'Boards this user can access' ) } aria-label={ t( 'Boards this user can access' ) } placeholder={ t( 'No boards selected' ) } searchable clearable value={ boardIds } onChange={ setBoardIds } data={ boards.map( board => ( { value: board.id, label: board.name } ) ) } />
                <Checkbox label={ t( 'Disabled account' ) } checked={ disabled } onChange={ event => { setDisabled( event.currentTarget.checked ); } } />
                <PasswordInput label={ t( 'Reset password' ) } autoComplete="new-password" description={ t( 'Leave blank to keep the current password' ) } minLength={ 14 } maxLength={ 128 } value={ resetPassword } onChange={ event => { setResetPassword( event.currentTarget.value ); } } />
                <Group justify="flex-end"><Button variant="default" onClick={ () => { setSelected( null ); } }>{ t( 'Cancel' ) }</Button><Button type="submit" loading={ busy }>{ t( 'Save account' ) }</Button></Group>
            </Stack></form>
        </Modal> }
    </Modal>;
}
