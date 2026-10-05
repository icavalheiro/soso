import { lazy, Suspense, useEffect, useState, useDeferredValue } from 'react';
import { ActionIcon, Avatar, Badge, Button, Group, Loader, Modal, PasswordInput, Popover, Select, Stack, Text, TextInput, Tooltip, useComputedColorScheme, useMantineColorScheme } from '@mantine/core';
import { Archive, Columns3, Languages, Plus, Search, Settings, LogOut, Sun, Moon, PanelLeftClose, PanelLeftOpen, RefreshCw, Users, SlidersHorizontal } from 'lucide-react';
import { api, ApiError, refreshCsrf, imageUrl, ticketBody, tags } from './api';
import type { Account, Board, BoardData, Ticket } from './api';
import { reportError } from './feedback';
import { BoardIcon } from './BoardIcon';
import { useLanguage } from './language';
const Kanban = lazy( () => import( './Kanban' ).then( module => ( { default: module.Kanban } ) ) );
const TicketModal = lazy( () => import( './TicketModal' ).then( module => ( { default: module.TicketModal } ) ) );
const BoardModal = lazy( () => import( './BoardModal' ).then( module => ( { default: module.BoardModal } ) ) );
const ProfileModal = lazy( () => import( './SettingsModals' ).then( module => ( { default: module.ProfileModal } ) ) );
const AdminModal = lazy( () => import( './SettingsModals' ).then( module => ( { default: module.AdminModal } ) ) );
const ArchiveModal = lazy( () => import( './ArchiveModal' ).then( module => ( { default: module.ArchiveModal } ) ) );

function IconButton ( { label, children, onClick, expanded, controls }: { label: string; children: React.ReactNode; onClick: () => void; expanded?: boolean; controls?: string; } )
{
    return <Tooltip label={ label }><ActionIcon aria-label={ label } aria-expanded={ expanded } aria-controls={ controls } variant="subtle" color="gray" size="sm" onClick={ onClick }>{ children }</ActionIcon></Tooltip>;
}

function LanguageButton ()
{
    const { language, setLanguage, t } = useLanguage();
    const [ opened, setOpened ] = useState( false );
    return <Popover opened={ opened } onChange={ setOpened } position="bottom-end" withArrow shadow="md">
        <Popover.Target><div style={ { display: 'flex', alignItems: 'center' } }><IconButton label={ t( 'Choose language' ) } onClick={ () => { setOpened( !opened ); } } expanded={ opened }><Languages size={ 15 } /></IconButton></div></Popover.Target>
        <Popover.Dropdown aria-label={ t( 'Choose language' ) } p="xs"><Stack gap={ 4 }>
            <Button variant={ language === 'en' ? 'light' : 'subtle' } justify="flex-start" size="xs" onClick={ () => { setLanguage( 'en' ); setOpened( false ); } }><span aria-hidden="true">🇺🇸</span>&nbsp; { t( 'English' ) }</Button>
            <Button variant={ language === 'pt-BR' ? 'light' : 'subtle' } justify="flex-start" size="xs" onClick={ () => { setLanguage( 'pt-BR' ); setOpened( false ); } }><span aria-hidden="true">🇧🇷</span>&nbsp; { t( 'Português (Brasil)' ) }</Button>
            <Button variant={ language === 'es-MX' ? 'light' : 'subtle' } justify="flex-start" size="xs" onClick={ () => { setLanguage( 'es-MX' ); setOpened( false ); } }><span aria-hidden="true">🇲🇽</span>&nbsp; { t( 'Español (México)' ) }</Button>
        </Stack></Popover.Dropdown>
    </Popover>;
}

export default function Workspace ()
{
    const [ account, setAccount ] = useState<Account | null>( null );
    const { t } = useLanguage();
    const [ booting, setBooting ] = useState( true );
    const [ boards, setBoards ] = useState<Board[]>( [] );
    const [ activeId, setActiveId ] = useState( '' );
    const [ loadedData, setData ] = useState<BoardData | null>( null );
    const data = loadedData?.board.id === activeId ? loadedData : null;
    const accountId = account?.id;
    const accountTheme = account?.theme;
    const [ collapsed, setCollapsed ] = useState( () =>
    {
        const stored = localStorage.getItem( 'soso-sidebar-collapsed' );
        return stored === null ? window.innerWidth < 768 : stored === 'true';
    } );
    const [ search, setSearch ] = useState( '' );
    const query = useDeferredValue( search );
    const [ priority, setPriority ] = useState( 'all' );
    const [ tagFilter, setTagFilter ] = useState<string[]>( [] );
    const [ assignee, setAssignee ] = useState( 'all' );
    const [ filtersOpen, setFiltersOpen ] = useState( false );
    const [ archive, setArchive ] = useState( false );
    const [ selected, setSelected ] = useState<Ticket | null>( null );
    const [ boardModal, setBoardModal ] = useState<'create' | 'edit' | null>( null );
    const [ profile, setProfile ] = useState( false );
    const [ admin, setAdmin ] = useState( false );
    const [ newColumn, setNewColumn ] = useState<string | null>( null );
    const [ title, setTitle ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ boardLoading, setBoardLoading ] = useState( false );
    const { setColorScheme } = useMantineColorScheme();
    const colorScheme = useComputedColorScheme( 'light', { getInitialValueInEffect: false } );

    useEffect( () =>
    {
        let alive = true;
        api<Account>( '/auth/me' ).then( async user =>
        {
            await refreshCsrf();
            if ( alive )
            {
                setAccount( user );
            }
        } ).catch( error =>
        {
            const expected = error instanceof ApiError && error.status === 401;
            if ( !expected )
            {
                reportError( error );
            }
        } ).finally( () => { if ( alive ) { setBooting( false ); } } );
        const expire = () => { setAccount( null ); setData( null ); setBoards( [] ); setSelected( null ); setProfile( false ); setAdmin( false ); setBoardModal( null ); };
        window.addEventListener( 'soso-session-expired', expire );
        return () => { alive = false; window.removeEventListener( 'soso-session-expired', expire ); };
    }, [] );

    useEffect( () =>
    {
        if ( !accountId )
        {
            return;
        }
        let alive = true;
        api<Board[]>( '/boards' ).then( result =>
        {
            if ( alive )
            {
                setBoards( result );
                const stored = localStorage.getItem( `soso-board-${ accountId }` );
                setActiveId( result.some( board => board.id === stored ) ? stored! : result[ 0 ]?.id ?? '' );
            }
        } ).catch( reportError );
        return () => { alive = false; };
    }, [ accountId ] );

    useEffect( () =>
    {
        if ( accountTheme )
        {
            setColorScheme( accountTheme );
        }
    }, [ accountTheme, setColorScheme ] );

    useEffect( () =>
    {
        if ( !activeId || !accountId )
        {
            return;
        }
        localStorage.setItem( `soso-board-${ accountId }`, activeId );
        let alive = true;
        api<BoardData>( `/boards/${ activeId }` ).then( result =>
        {
            if ( alive )
            {
                setData( result );
            }
        } ).catch( reportError ).finally( () => { if ( alive ) { setBoardLoading( false ); } } );
        return () => { alive = false; };
    }, [ activeId, accountId ] );

    function toggleSidebar ()
    {
        const next = !collapsed;
        setCollapsed( next );
        localStorage.setItem( 'soso-sidebar-collapsed', String( next ) );
    }

    function selectBoard ( id: string )
    {
        setActiveId( id ); setSelected( null ); setSearch( '' ); setAssignee( 'all' ); setTagFilter( [] ); setArchive( false );
        if ( window.innerWidth < 768 )
        {
            setCollapsed( true );
            localStorage.setItem( 'soso-sidebar-collapsed', 'true' );
        }
    }

    async function reload ()
    {
        if ( !activeId )
        {
            return;
        }
        try
        {
            setBoardLoading( true );
            setData( await api<BoardData>( `/boards/${ activeId }` ) );
        }
        catch ( error )
        {
            reportError( error );
        }
        finally
        {
            setBoardLoading( false );
        }
    }

    function changed ( ticket: Ticket )
    {
        setData( previous => previous ? { ...previous, tickets: previous.tickets.map( item => item.id === ticket.id ? ticket : item ) } : previous );
    }

    async function move ( ticket: Ticket, columnId: string, position: number )
    {
        try
        {
            changed( await api<Ticket>( `/boards/${ ticket.boardId }/tickets/${ ticket.id }`, 'PUT', { ...ticketBody( ticket ), columnId, position } ) );
        }
        catch ( error )
        {
            reportError( error );
            await reload();
        }
    }

    async function createTicket ()
    {
        try
        {
            setBusy( true );
            const ticket = await api<Ticket>( `/boards/${ activeId }/tickets`, 'POST', { title, columnId: newColumn } );
            setData( previous => previous ? { ...previous, tickets: [ ...previous.tickets, ticket ] } : previous );
            setTitle( '' ); setNewColumn( null ); setSelected( ticket );
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

    async function toggleTheme ()
    {
        const next = colorScheme === 'dark' ? 'light' : 'dark';
        setColorScheme( next );
        if ( !account )
        {
            return;
        }
        try
        {
            setAccount( await api<Account>( '/auth/profile', 'PUT', { name: account.name, settings: account.settings ?? '', theme: next } ) );
        }
        catch ( error )
        {
            setColorScheme( account.theme ); reportError( error );
        }
    }

    const themeButton = <Group gap={ 4 } wrap="nowrap"><LanguageButton /><IconButton label={ t( colorScheme === 'dark' ? 'Light theme' : 'Dark theme' ) } onClick={ () => { void toggleTheme(); } }>{ colorScheme === 'dark' ? <Sun size={ 15 } /> : <Moon size={ 15 } /> }</IconButton></Group>;

    if ( booting )
    {
        return <div className="loading-screen"><Loader /></div>;
    }
    if ( !account )
    {
        return <Login onLogin={ setAccount } themeButton={ themeButton } />;
    }
    const canManage = data?.board.ownerId === account.id || account.isAdmin;
    const filtered = data?.tickets.filter( ticket =>
    {
        const matchesQuery = `${ ticket.id } ${ ticket.title } ${ ticket.description }`.toLowerCase().includes( query.toLowerCase() );
        const matchesPriority = priority === 'all' || ticket.priority === priority;
        const matchesTags = tagFilter.length === 0 || tagFilter.some( tag => ticket.tags.includes( tag ) );
        const matchesAssignee = assignee === 'all' || ( assignee === 'unassigned' ? ticket.assigneeId === null : ticket.assigneeId === assignee );
        return !ticket.archived && matchesQuery && matchesPriority && matchesTags && matchesAssignee;
    } ) ?? [];
    const activeFilters = tagFilter.length + Number( priority !== 'all' ) + Number( assignee !== 'all' );

    return <div className={ `app-shell ${ collapsed ? 'is-collapsed' : '' }` }>
        <aside className="sidebar" id="workspace-sidebar" aria-label={ t( 'Workspace navigation' ) }>
            <div className="brand"><img src="/logo.jpg" alt="Sosô" /><strong>Sosô<span>{ t( 'Organizando tua vida :D' ) }</span></strong></div>
            <div className="sidebar-heading"><span>{ t( 'BOARDS' ) }</span><IconButton label={ t( 'Create board' ) } onClick={ () => { setBoardModal( 'create' ); } }><Plus size={ 17 } /></IconButton></div>
            <nav className="board-nav">{ boards.map( board => <button key={ board.id } className={ `board-link ${ board.id === activeId ? 'active' : '' }` } aria-current={ board.id === activeId ? 'page' : undefined } onClick={ () => { selectBoard( board.id ); } }><BoardIcon icon={ board.icon } color={ board.color } /><span>{ board.name }</span></button> ) }</nav>
            <div className="sidebar-bottom">
                { account.isAdmin && <button className="board-link" aria-label={ t( 'Accounts' ) } onClick={ () => { setAdmin( true ); } }><Users size={ 18 } /><span>{ t( 'Accounts' ) }</span></button> }
                <button className="board-link" aria-label={ t( 'Profile & settings' ) } onClick={ () => { setProfile( true ); } }><Settings size={ 18 } /><span>{ t( 'Profile & settings' ) }</span></button>
                <button className="account-button" aria-label={ t( 'Open profile' ) } onClick={ () => { setProfile( true ); } }><Avatar src={ imageUrl( account.avatarId ) } size={ 32 } radius="xl">{ account.name.slice( 0, 2 ).toUpperCase() }</Avatar><span><strong>{ account.name }</strong><small>{ t( account.isAdmin ? 'Administrator' : 'Member' ) }</small></span></button>
            </div>
        </aside>
        <main className="workspace">
            <header className="topbar">
                <Group gap={ 6 } wrap="nowrap" className="topbar-title"><IconButton label={ t( collapsed ? 'Expand sidebar' : 'Collapse sidebar' ) } onClick={ toggleSidebar } expanded={ !collapsed } controls="workspace-sidebar">{ collapsed ? <PanelLeftOpen size={ 16 } /> : <PanelLeftClose size={ 16 } /> }</IconButton><span className="workspace-label">Sosô</span><span className="separator">/</span>{ data && <BoardIcon icon={ data.board.icon } color={ data.board.color } size={ 14 } /> }<Text className="topbar-board-name" size="xs" fw={ 600 } truncate>{ data?.board.name ?? t( 'Boards' ) }</Text>{ data && <Badge className="board-ticket-count" variant="light" color="gray" size="sm">{ filtered.length } { t( filtered.length === 1 ? 'ticket' : 'tickets' ) }</Badge> }</Group>
                <Group gap={ 4 } wrap="nowrap" className="topbar-actions">
                    { data && <TextInput className="topbar-search" aria-label={ t( 'Search tickets' ) } placeholder={ t( 'Search cards...' ) } leftSection={ <Search size={ 14 } /> } size="xs" value={ search } onChange={ event => { setSearch( event.currentTarget.value ); } } /> }
                    { themeButton }
                    <IconButton label={ t( 'Sign out' ) } onClick={ () => { void api( '/auth/logout', 'POST' ).then( () => { setAccount( null ); setData( null ); setBoards( [] ); } ).catch( reportError ); } }><LogOut size={ 15 } /></IconButton>
                </Group>
            </header>
            { data ? <><section className="board-heading">
                <div className="board-title"><h1>{ data.board.name }</h1>{ data.board.description && <p>{ data.board.description }</p> }</div>
                <Group className="board-actions" gap={ 6 }>
                    <Avatar.Group className="board-avatars">{ data.members.slice( 0, 4 ).map( member => <Tooltip key={ member.id } label={ member.name }><Avatar size={ 24 } radius="xl" src={ imageUrl( member.avatarId ) }>{ member.name.slice( 0, 1 ) }</Avatar></Tooltip> ) }</Avatar.Group>
                    { canManage && <IconButton label={ t( 'Board settings' ) } onClick={ () => { setBoardModal( 'edit' ); } }><Settings size={ 16 } /></IconButton> }
                    <IconButton label={ t( 'Refresh board' ) } onClick={ () => { void reload(); } }>{ boardLoading ? <Loader size={ 14 } /> : <RefreshCw size={ 15 } /> }</IconButton>
                    <Button size="xs" variant={ activeFilters > 0 ? 'light' : 'default' } leftSection={ <SlidersHorizontal size={ 14 } /> } aria-haspopup="dialog" onClick={ () => { setFiltersOpen( true ); } }>{ t( 'Filters' ) }{ activeFilters > 0 ? ` (${ activeFilters })` : '' }</Button>
                    <Button size="xs" variant="default" leftSection={ <Archive size={ 14 } /> } onClick={ () => { setArchive( true ); } }>{ t( 'Archive' ) }</Button>
                    <Button size="xs" leftSection={ <Plus size={ 14 } /> } onClick={ () => { setNewColumn( data.board.columns[ 0 ].id ); } }>{ t( 'New ticket' ) }</Button>
                </Group>
            </section>
                <Suspense fallback={ <Loader m="xl" /> }><Kanban data={ data } tickets={ filtered } onOpen={ setSelected } onCreate={ setNewColumn } onMove={ move } /></Suspense></> : <div className="empty-workspace">{ boardLoading || activeId ? <Loader /> : <><Columns3 size={ 42 } strokeWidth={ 1.2 } /><h1>{ t( 'Your workspace, ready.' ) }</h1><Button leftSection={ <Plus size={ 17 } /> } onClick={ () => { setBoardModal( 'create' ); } }>{ t( 'Create a board' ) }</Button></> }</div> }
        </main>
        <Modal opened={ filtersOpen && data !== null } onClose={ () => { setFiltersOpen( false ); } } title={ t( 'Filters' ) } centered size="sm">
            <Stack>
                <div><Text size="sm" fw={ 500 } mb={ 8 }>{ t( 'Tags' ) }</Text><div className="tag-filters">{ tags.map( tag => <button key={ tag.value } className={ `tag-filter ${ tagFilter.includes( tag.value ) ? 'selected' : '' }` } aria-pressed={ tagFilter.includes( tag.value ) } onClick={ () => { setTagFilter( previous => previous.includes( tag.value ) ? previous.filter( value => value !== tag.value ) : [ ...previous, tag.value ] ); } }><i style={ { background: tag.color } } />{ t( tag.label ) }</button> ) }</div></div>
                <Select label={ t( 'Assignee' ) } aria-label={ t( 'Filter by assignee' ) } value={ assignee } onChange={ value => { setAssignee( value ?? 'all' ); } } data={ [ { label: t( 'All assignees' ), value: 'all' }, { label: t( 'Unassigned' ), value: 'unassigned' }, ...( data?.members ?? [] ).map( member => ( { value: member.id, label: member.name } ) ) ] } allowDeselect={ false } />
                <Select label={ t( 'Priority' ) } aria-label={ t( 'Filter by priority' ) } value={ priority } onChange={ value => { setPriority( value ?? 'all' ); } } data={ [ { label: t( 'All priorities' ), value: 'all' }, ...[ 'urgent', 'high', 'normal', 'low' ].map( value => ( { value, label: t( value[ 0 ].toUpperCase() + value.slice( 1 ) ) } ) ) ] } allowDeselect={ false } />
                <Group justify="space-between"><Button variant="subtle" disabled={ activeFilters === 0 } onClick={ () => { setPriority( 'all' ); setAssignee( 'all' ); setTagFilter( [] ); } }>{ t( 'Clear filters' ) }</Button><Button onClick={ () => { setFiltersOpen( false ); } }>{ t( 'Done' ) }</Button></Group>
            </Stack>
        </Modal>
        <Suspense fallback={ <Loader className="modal-loading" /> }>
            { selected && data && <TicketModal key={ selected.id } ticket={ selected } data={ data } account={ account } onClose={ () => { setSelected( null ); } } onChange={ changed } onDelete={ id => { setData( previous => previous ? { ...previous, tickets: previous.tickets.filter( ticket => ticket.id !== id ) } : previous ); setSelected( null ); } } /> }
            { boardModal && <BoardModal board={ boardModal === 'edit' ? data?.board : undefined } onClose={ () => { setBoardModal( null ); } } onSave={ board => { setBoards( previous => [ ...previous.filter( item => item.id !== board.id ), board ] ); selectBoard( board.id ); setBoardModal( null ); void api<BoardData>( `/boards/${ board.id }` ).then( setData ).catch( reportError ); } } onDelete={ id => { const remaining = boards.filter( board => board.id !== id ); setBoards( remaining ); selectBoard( remaining[ 0 ]?.id ?? '' ); setData( null ); setBoardModal( null ); } } /> }
            { profile && <ProfileModal account={ account } onChange={ setAccount } onClose={ () => { setProfile( false ); } } /> }
            { admin && <AdminModal onClose={ () => { setAdmin( false ); } } /> }
            { archive && data && <ArchiveModal data={ data } onClose={ () => { setArchive( false ); } } onOpen={ setSelected } onChange={ changed } /> }
        </Suspense>
        <Modal opened={ newColumn !== null } onClose={ () => { setNewColumn( null ); } } title={ t( 'New ticket' ) } centered><form onSubmit={ event => { event.preventDefault(); void createTicket(); } }><Stack><TextInput label={ t( 'Title' ) } placeholder={ t( 'What needs to happen?' ) } required maxLength={ 160 } value={ title } autoFocus onChange={ event => { setTitle( event.currentTarget.value ); } } /><Select label={ t( 'Column' ) } value={ newColumn } onChange={ setNewColumn } data={ data?.board.columns.map( column => ( { value: column.id, label: column.name } ) ) ?? [] } allowDeselect={ false } /><Button type="submit" loading={ busy } disabled={ !title.trim() }>{ t( 'Create ticket' ) }</Button></Stack></form></Modal>
    </div>;
}

function Login ( { onLogin, themeButton }: { onLogin: ( account: Account ) => void; themeButton: React.ReactNode; } )
{
    const { t } = useLanguage();
    const [ email, setEmail ] = useState( '' );
    const [ password, setPassword ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ error, setError ] = useState( '' );
    async function submit ( event: React.FormEvent )
    {
        event.preventDefault(); setBusy( true ); setError( '' );
        try
        {
            await refreshCsrf();
            const account = await api<Account>( '/auth/login', 'POST', { email, password } );
            await refreshCsrf();
            onLogin( account );
        }
        catch ( failure )
        {
            setError( failure instanceof Error ? failure.message : t( 'Unable to sign in.' ) );
        }
        finally
        {
            setBusy( false );
        }
    }
    return <main className="login-page"><div className="login-brand"><img src="/logo.jpg" alt="Sosô" /><h1>Sosô</h1>{ themeButton }</div><form className="login-form" onSubmit={ event => { void submit( event ); } }><h2>{ t( 'Sign in' ) }</h2><Stack gap="md"><TextInput label={ t( 'Email' ) } type="email" autoComplete="username" required maxLength={ 254 } value={ email } onChange={ event => { setEmail( event.currentTarget.value ); } } /><PasswordInput label={ t( 'Password' ) } autoComplete="current-password" required maxLength={ 128 } value={ password } onChange={ event => { setPassword( event.currentTarget.value ); } } />{ error && <Text role="alert" c="red" size="sm">{ error }</Text> }<Button type="submit" loading={ busy }>{ t( 'Sign in' ) }</Button></Stack></form></main>;
}
