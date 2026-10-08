export type Account = { id: string; email: string; name: string; isAdmin: boolean; disabled: boolean; avatarId: string | null; theme: 'light' | 'dark'; settings: string; boardIds: string[] | null; };
export type Person = Pick<Account, 'id' | 'name' | 'avatarId'>;
export type Column = { id: string; name: string; isDone: boolean; };
export type Board = { id: string; name: string; description: string; icon: string; color: string; ownerId: string; members: string[]; columns: Column[]; removedColumns?: { column: Column; position: number; }[]; revision: number; };
export type Subtask = { id: string; title: string; done: boolean; };
export type Comment = { id: string; authorId: string; text: string; createdAt: string; };
export type TicketActivity = { id: string; actorId: string; actorName: string; action: string; field: string | null; oldValue: string | null; newValue: string | null; createdAt: string; };
export type Ticket = { id: string; boardId: string; columnId: string; title: string; description: string; priority: string; tags: string[]; archived: boolean; assigneeId: string | null; dueDate: string | null; position: number; revision: number; subtasks: Subtask[]; comments: Comment[]; activity: TicketActivity[]; images: string[]; videos: string[]; };
export type BoardData = { board: Board; tickets: Ticket[]; members: Person[]; };
export type Token = { id: string; name: string; expiresAt: string; boardIds: string[]; };
export type BoardChangedEvent = { boardId: string | null; };
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
    return { title, description: description ?? '', columnId, priority, assigneeId, dueDate, position, subtasks, tags, archived, revision };
}

export function connectBoardEvents ( onChange: ( event: BoardChangedEvent ) => void, onReconnect: () => void )
{
    let stopped = false;
    let socket: WebSocket | null = null;
    let retryTimer = 0;
    let retryDelay = 500;

    function connect ()
    {
        if ( stopped )
        {
            return;
        }
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        socket = new WebSocket( `${ protocol }//${ window.location.host }/api/events` );
        socket.onopen = () =>
        {
            retryDelay = 500;
            onReconnect();
        };
        socket.onmessage = message =>
        {
            try
            {
                const event = JSON.parse( String( message.data ) ) as BoardChangedEvent;
                if ( event.boardId === null || typeof event.boardId === 'string' )
                {
                    onChange( event );
                }
            }
            catch
            {
                // Ignore malformed events and keep the connection alive.
            }
        };
        socket.onclose = () => scheduleReconnect();
        socket.onerror = () =>
        {
            if ( socket?.readyState !== WebSocket.CLOSED )
            {
                socket?.close();
            }
            else
            {
                scheduleReconnect();
            }
        };
    }

    function scheduleReconnect ()
    {
        if ( stopped || retryTimer )
        {
            return;
        }
        retryTimer = window.setTimeout( () =>
        {
            retryTimer = 0;
            connect();
        }, retryDelay );
        retryDelay = Math.min( retryDelay * 2, 15_000 );
    }

    connect();
    return () =>
    {
        stopped = true;
        window.clearTimeout( retryTimer );
        socket?.close();
    };
}

export function videoUrl ( id: string | null | undefined )
{
    return id ? `/api/videos/${ id }` : undefined;
}

export function videoThumbnailUrl ( id: string | null | undefined )
{
    return id ? `/api/videos/${ id }/thumbnail` : undefined;
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
