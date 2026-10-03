import type { Page } from '@playwright/test';
import type { Account, BoardData } from '../src/api';

export function fixture (): { account: Account; data: BoardData }
{
    const account: Account = { id: 'admin-fixture', name: 'Maya Chen', email: 'maya@example.test', isAdmin: true, disabled: false, avatarId: null, theme: 'light', settings: '' };
    const data: BoardData = {
        board: { id: 'board-fixture', name: 'Soso development', description: 'Sprint 01', ownerId: account.id, members: [ 'sam-fixture' ], revision: 0, columns: [ { id: 'todo', name: 'To do', isDone: false }, { id: 'progress', name: 'In progress', isDone: false }, { id: 'done', name: 'Done', isDone: true } ] },
        members: [ account, { id: 'sam-fixture', name: 'Sam Rivers', avatarId: null } ], tickets: [],
    };
    const entries = [
        { title: 'Sketch the board layout', description: 'Refine spacing, card density and the ticket dialog.', columnId: 'todo', tags: [ 'design' ], assigneeId: account.id },
        { title: 'Plan sprint goals', description: 'Pick the highest-impact work for the next two weeks.', columnId: 'todo', tags: [ 'feature' ], assigneeId: account.id },
        { title: 'Fix drag-and-drop glitch', description: 'Cards sometimes jump when dropped at the bottom of a column.', columnId: 'progress', tags: [ 'bug' ], assigneeId: 'sam-fixture' },
        { title: 'Set up repository', description: 'Repository, README and basic tooling are in place.', columnId: 'done', tags: [ 'feature', 'docs' ], assigneeId: 'sam-fixture' },
        { title: 'Update dependency audit', description: 'Review the dependency report before the release.', columnId: 'done', tags: [ 'chore' ], assigneeId: account.id, archived: true },
    ];
    data.tickets = entries.map( ( entry, index ) => ( { id: `ticket-${ index }`, boardId: data.board.id, priority: index === 2 ? 'urgent' : 'normal', archived: false, dueDate: null, position: ( index + 1 ) * 1024, revision: 0, subtasks: [], comments: [], images: [], ...entry } ) );
    return { account, data };
}

export async function installApiMock ( page: Page, authenticated = true )
{
    const state = fixture();
    let loggedIn = authenticated;
    let sequence = 10;
    const accounts = [ state.account ];
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
            Object.assign( state.account, request.postDataJSON() );
            return reply( state.account );
        }
        if ( path === '/api/auth/tokens' )
        {
            return reply( [] );
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
                const account = { ...state.account, ...input, id: `account-${ sequence++ }` };
                accounts.push( account );
                return reply( account, 201 );
            }
            return reply( accounts );
        }
        if ( path === '/api/boards' )
        {
            return reply( [ state.data.board ] );
        }
        if ( path === `/api/boards/${ state.data.board.id }` )
        {
            return reply( state.data );
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
            ticket.comments.push( { id: `comment-${ sequence++ }`, authorId: state.account.id, text: request.postDataJSON().text, createdAt: new Date().toISOString() } );
            ticket.revision++;
            return reply( ticket );
        }
        if ( ticket && path.endsWith( '/images' ) )
        {
            ticket.images.push( `image-${ sequence++ }` );
            ticket.revision++;
            return reply( ticket );
        }
        return reply( { detail: `Unmocked request: ${ method } ${ path }` }, 404 );
    } );
    return state;
}