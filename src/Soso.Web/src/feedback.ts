import { notifications } from '@mantine/notifications';
import { ApiError } from './api';

export function reportError ( error: unknown )
{
    notifications.show( { title: 'Something needs attention', message: error instanceof Error ? error.message : 'Request failed.', color: 'red' } );
    const expired = error instanceof ApiError && error.status === 401;
    if ( expired )
    {
        window.dispatchEvent( new Event( 'soso-session-expired' ) );
    }
}