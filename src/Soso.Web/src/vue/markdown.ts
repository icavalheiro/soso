import DOMPurify from 'dompurify';
import { Marked, Renderer } from 'marked';

export type MarkdownMode = 'excerpt' | 'comment' | 'full';

// Renders GFM (tables, strikethrough, task lists), skips raw HTML, drops external images
// and removes javascript: links. Inline ticket images (/api/images/*)
// render as preview buttons; the host component handles clicks with a
// delegated listener on [data-image-id].
function escapeAttr ( value: string ): string
{
    return value.replaceAll( '&', '&amp;' ).replaceAll( '"', '&quot;' ).replaceAll( '<', '&lt;' ).replaceAll( '>', '&gt;' );
}

function rendererFor ( mode: MarkdownMode ): Record<string, ( this: Renderer, token: never ) => string >
{
    const overrides: Record<string, ( this: Renderer, token: never ) => string > = {
        link ( token )
        {
            if ( mode === 'full' )
            {
                return Renderer.prototype.link.call( this, token );
            }
            const inline = token as { tokens?: unknown[] };
            return this.parser.parseInline( ( inline.tokens ?? [] ) as never );
        },
        image ( token )
        {
            const image = token as { href?: string; text?: string };
            const href = image.href ?? '';
            if ( ( mode === 'comment' || mode === 'full' ) && href.startsWith( '/api/images/' ) )
            {
                const id = href.slice( '/api/images/'.length );
                return `<button type="button" class="ticket-inline-image" data-image-id="${ escapeAttr( id ) }"><img src="${ escapeAttr( href ) }" alt="${ escapeAttr( image.text || 'Ticket attachment' ) }"></button>`;
            }
            return '';
        },
        html ()
        {
            return '';
        },
    };
    if ( mode !== 'full' )
    {
        // Task-list checkboxes are inline tokens; drop them outside full rendering.
        overrides.checkbox = () => '';
    }
    return overrides;
}

export function renderMarkdown ( source: string, mode: MarkdownMode ): string
{
    const dirty = new Marked().use( { renderer: rendererFor( mode ) } ).parse( source );
    return DOMPurify.sanitize( dirty as string, { ADD_TAGS: [ 'input' ], ADD_ATTR: [ 'checked', 'disabled', 'type', 'class', 'data-image-id' ] } );
}
