export type Account = { id: string; email: string; name: string; isAdmin: boolean; disabled: boolean; avatarId: string | null; theme: 'light' | 'dark'; settings: string; };
export type Person = Pick<Account, 'id' | 'name' | 'avatarId'>;
export type Column = { id: string; name: string; isDone: boolean; };
export type Board = { id: string; name: string; description: string; ownerId: string; members: string[]; columns: Column[]; revision: number; };
export type Subtask = { id: string; title: string; done: boolean; };
export type Comment = { id: string; authorId: string; text: string; createdAt: string; };
export type Ticket = { id: string; boardId: string; columnId: string; title: string; description: string; priority: string; tags: string[]; archived: boolean; assigneeId: string | null; dueDate: string | null; position: number; revision: number; subtasks: Subtask[]; comments: Comment[]; images: string[]; };
export type BoardData = { board: Board; tickets: Ticket[]; members: Person[]; };
export type Token = { id: string; name: string; expiresAt: string; };
let csrfToken = '';

export async function refreshCsrf ()
{
    const response = await fetch( '/api/auth/csrf', { credentials: 'same-origin' } );
    if ( !response.ok )
    {
        throw new Error( 'Unable to establish a secure session.' );
    }
    const data = await response.json();
    csrfToken = data.token;
}

export class ApiError extends Error
{
    status: number;
    constructor ( status: number, message: string )
    {
        super( message );
        this.status = status;
    }
}

export async function api<T> ( path: string, method = 'GET', body?: unknown ): Promise<T>
{
    const headers: Record<string, string> = {};
    const isForm = body instanceof FormData;
    if ( method !== 'GET' )
    {
        if ( !csrfToken )
        {
            await refreshCsrf();
        }
        headers[ 'X-CSRF-TOKEN' ] = csrfToken;
    }
    if ( body !== undefined && !isForm )
    {
        headers[ 'Content-Type' ] = 'application/json';
    }
    const response = await fetch( `/api${ path }`, {
        method, headers, credentials: 'same-origin',
        body: body === undefined ? undefined : isForm ? body as FormData : JSON.stringify( body ),
    } );
    if ( !response.ok )
    {
        const problem = await response.json().catch( () => ( {} ) );
        const validation = problem.errors ? Object.values( problem.errors ).flat().join( ' ' ) : '';
        throw new ApiError( response.status, validation || problem.detail || problem.title || 'Request failed.' );
    }
    if ( response.status === 204 )
    {
        return undefined as T;
    }
    return response.json() as Promise<T>;
}

export function imageUrl ( id: string | null | undefined )
{
    return id ? `/api/images/${ id }` : undefined;
}

export function newId ()
{
    return crypto.randomUUID().replaceAll( '-', '' );
}

export function ticketBody ( ticket: Ticket )
{
    const { title, description, columnId, priority, assigneeId, dueDate, position, subtasks, tags, archived, revision } = ticket;
    return { title, description, columnId, priority, assigneeId, dueDate, position, subtasks, tags, archived, revision };
}

export const tags = [
    { value: 'bug', label: 'Bug', color: '#df5766' },
    { value: 'feature', label: 'Feature', color: '#4285df' },
    { value: 'design', label: 'Design', color: '#8b64cf' },
    { value: 'docs', label: 'Docs', color: '#36a092' },
    { value: 'refactor', label: 'Refactor', color: '#e69b2c' },
    { value: 'test', label: 'Test', color: '#549646' },
    { value: 'chore', label: 'Chore', color: '#85909c' },
    { value: 'research', label: 'Research', color: '#c878a0' },
];