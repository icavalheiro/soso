import type { Page } from '@playwright/test';
import type { Account, BoardData, Token } from '../src/api';

export function fixture (): { account: Account; data: BoardData; }
{
    const account: Account = { id: 'admin-fixture', name: 'Maya Chen', email: 'maya@example.test', isAdmin: true, disabled: false, avatarId: null, theme: 'light', settings: '', boardIds: null };
    const data: BoardData = {
        board: { id: 'board-fixture', name: 'Soso development', description: 'Sprint 01', icon: 'columns', color: 'teal', ownerId: account.id, members: [ 'sam-fixture' ], revision: 0, columns: [ { id: 'todo', name: 'To do', isDone: false }, { id: 'progress', name: 'In progress', isDone: false }, { id: 'done', name: 'Done', isDone: true } ] },
        members: [ account, { id: 'sam-fixture', name: 'Sam Rivers', avatarId: null } ], tickets: [],
    };
    const entries = [
        { title: 'Sketch the board layout', description: 'Refine spacing, card density and the ticket dialog.', columnId: 'todo', tags: [ 'design' ], assigneeId: account.id },
        { title: 'Plan sprint goals', description: 'Pick the highest-impact work for the next two weeks.', columnId: 'todo', tags: [ 'feature' ], assigneeId: account.id },
        { title: 'Fix drag-and-drop glitch', description: 'Cards sometimes jump when dropped at the bottom of a column.', columnId: 'progress', tags: [ 'bug' ], assigneeId: 'sam-fixture' },
        { title: 'Set up repository', description: 'Repository, README and basic tooling are in place.', columnId: 'done', tags: [ 'feature', 'docs' ], assigneeId: 'sam-fixture' },
        { title: 'Update dependency audit', description: 'Review the dependency report before the release.', columnId: 'done', tags: [ 'chore' ], assigneeId: account.id, archived: true },
    ];
    data.tickets = entries.map( ( entry, index ) => ( { id: `ticket-${ index }`, boardId: data.board.id, priority: index === 2 ? 'urgent' : 'normal', archived: false, dueDate: null, position: ( index + 1 ) * 1024, revision: 0, subtasks: [], comments: [], activity: [], images: [], videos: [], ...entry } ) );
    return { account, data };
}

export async function installApiMock ( page: Page, authenticated = true )
{
    const state = fixture();
    let loggedIn = authenticated;
    let sequence = 10;
    const accounts = [ state.account ];
    const tokens: Token[] = [];
    await page.route( '**/api/**', async route =>
    {
        const request = route.request();
        const path = new URL( request.url() ).pathname;
        const method = request.method();
        const reply = ( body: unknown, status = 200 ) => route.fulfill( { status, json: body } );
        if ( path === '/api/auth/csrf' )
        {
            return reply( { token: 'test-csrf-only' } );
        }
        if ( path === '/api/auth/login' )
        {
            loggedIn = true;
            return reply( state.account );
        }
        if ( !loggedIn )
        {
            return reply( { detail: 'Sign in required.' }, 401 );
        }
        if ( path === '/api/auth/me' || path === '/api/auth/avatar' )
        {
            return reply( state.account );
        }
        if ( path === '/api/auth/profile' )
        {
            const input = request.postDataJSON();
            const hasSettings = typeof input.settings === 'string';
            if ( !hasSettings )
            {
                return reply( { errors: { Settings: [ 'The Settings field is required.' ] } }, 400 );
            }
            Object.assign( state.account, input );
            return reply( state.account );
        }
        if ( path === '/api/auth/password' )
        {
            loggedIn = false;
            return route.fulfill( { status: 204 } );
        }
        if ( path === '/api/auth/logout' )
        {
            loggedIn = false;
            return route.fulfill( { status: 204 } );
        }
        if ( path === '/api/auth/tokens' )
        {
            if ( method === 'POST' )
            {
                const token = { id: `token-${ sequence++ }`, name: request.postDataJSON().name, expiresAt: '2026-11-03T00:00:00Z', boardIds: [] };
                tokens.push( token );
                return reply( { token: 'test-only-mcp-secret', expiresAt: token.expiresAt } );
            }
            return reply( tokens );
        }
        if ( path.startsWith( '/api/auth/tokens/' ) )
        {
            const token = tokens.find( item => path.split( '/' )[ 4 ] === item.id );
            if ( !token )
            {
                return reply( { detail: 'Token not found.' }, 404 );
            }
            if ( method === 'PUT' && path.endsWith( '/boards' ) )
            {
                token.boardIds = request.postDataJSON().boardIds;
                return reply( token );
            }
            if ( method === 'DELETE' )
            {
                tokens.splice( tokens.indexOf( token ), 1 );
                return route.fulfill( { status: 204 } );
            }
        }
        if ( path === '/api/people' )
        {
            return reply( state.data.members );
        }
        if ( path === '/api/admin/accounts' )
        {
            if ( method === 'POST' )
            {
                const input = request.postDataJSON();
                const account = { ...state.account, boardIds: null, ...input, id: `account-${ sequence++ }` };
                accounts.push( account );
                return reply( account, 201 );
            }
            if ( method === 'PUT' )
            {
                const id = path.split( '/' ).at( -1 );
                const account = accounts.find( item => item.id === id );
                if ( !account )
                {
                    return reply( { detail: 'Account not found.' }, 404 );
                }
                Object.assign( account, request.postDataJSON() );
                return reply( account );
            }
            return reply( accounts );
        }
        if ( path === '/api/boards' )
        {
            if ( method === 'POST' )
            {
                Object.assign( state.data.board, request.postDataJSON(), { id: `board-${ sequence++ }`, revision: 0 } );
                state.data.tickets = [];
                return reply( state.data.board, 201 );
            }
            return reply( [ state.data.board ] );
        }
        if ( path === `/api/boards/${ state.data.board.id }` )
        {
            if ( method === 'PUT' )
            {
                Object.assign( state.data.board, request.postDataJSON(), { revision: state.data.board.revision + 1 } );
                return reply( state.data.board );
            }
            return reply( state.data );
        }
        if ( path === `/api/boards/${ state.data.board.id }/tickets` && method === 'POST' )
        {
            const input = request.postDataJSON();
            const ticket = { id: `ticket-${ sequence++ }`, boardId: state.data.board.id, description: '', priority: 'normal', tags: [], archived: false, assigneeId: null, dueDate: null, position: ( state.data.tickets.length + 1 ) * 1024, revision: 0, subtasks: [], comments: [], activity: [ { id: `activity-${ sequence++ }`, actorId: state.account.id, actorName: state.account.name, action: 'created', field: null, oldValue: null, newValue: null, createdAt: new Date().toISOString() } ], images: [], videos: [], ...input };
            state.data.tickets.push( ticket );
            return reply( ticket, 201 );
        }
        if ( path.startsWith( '/api/videos/' ) && path.endsWith( '/thumbnail' ) )
        {
            return route.fulfill( { path: 'public/logo.jpg', contentType: 'image/jpeg' } );
        }
        if ( path.startsWith( '/api/videos/' ) )
        {
            return route.fulfill( { status: 200, contentType: 'video/mp4', body: Buffer.from( [ 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109 ] ) } );
        }
        if ( path.startsWith( '/api/images/' ) )
        {
            return route.fulfill( { path: 'public/logo.jpg', contentType: 'image/jpeg' } );
        }
        const ticket = state.data.tickets.find( ticket => path.includes( `/tickets/${ ticket.id }` ) );
        if ( ticket && method === 'PUT' )
        {
            Object.assign( ticket, request.postDataJSON(), { revision: ticket.revision + 1 } );
            return reply( ticket );
        }
        if ( ticket && path.endsWith( '/comments' ) )
        {
            const text = request.postDataJSON().text;
            ticket.comments.push( { id: `comment-${ sequence++ }`, authorId: state.account.id, text, createdAt: new Date().toISOString() } );
            ticket.activity.push( { id: `activity-${ sequence++ }`, actorId: state.account.id, actorName: state.account.name, action: 'comment_added', field: 'comment', oldValue: null, newValue: text, createdAt: new Date().toISOString() } );
            ticket.revision++;
            return reply( ticket );
        }
        if ( ticket && path.endsWith( '/images' ) )
        {
            ticket.images.push( `image-${ sequence++ }` );
            ticket.revision++;
            return reply( ticket );
        }
        if ( ticket && path.endsWith( '/videos' ) )
        {
            ticket.videos ??= [];
            ticket.videos.push( `video-${ sequence++ }` );
            ticket.revision++;
            return reply( ticket );
        }
        return reply( { detail: `Unmocked request: ${ method } ${ path }` }, 404 );
    } );
    return state;
}
