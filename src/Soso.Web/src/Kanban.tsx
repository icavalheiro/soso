import { useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { DndContext, DragOverlay, KeyboardSensor, PointerSensor, closestCorners, pointerWithin, useDroppable, useSensor, useSensors } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, useSortable, verticalListSortingStrategy, sortableKeyboardCoordinates } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ActionIcon, Avatar, Badge, Group, Progress, Tooltip } from '@mantine/core';
import { Plus, Check, CheckSquare, MessageSquare, CalendarDays, GripVertical } from 'lucide-react';
import { imageUrl, tags } from './api';
import type { BoardData, Column, Ticket } from './api';

export function Kanban ( { data, tickets, onOpen, onCreate, onMove }: { data: BoardData; tickets: Ticket[]; onOpen: ( ticket: Ticket ) => void; onCreate: ( column: string ) => void; onMove: ( ticket: Ticket, columnId: string, position: number ) => Promise<void>; } )
{
    const [ dragged, setDragged ] = useState<Ticket | null>( null );
    const sensors = useSensors( useSensor( PointerSensor, { activationConstraint: { distance: 6 } } ), useSensor( KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates } ) );
    function end ( event: DragEndEvent )
    {
        setDragged( null );
        const { active, over } = event;
        if ( !over || active.id === over.id )
        {
            return;
        }
        const ticket = data.tickets.find( ticket => ticket.id === active.id );
        const targetTicket = data.tickets.find( ticket => ticket.id === over.id );
        const column = data.board.columns.find( column => column.id === over.id );
        const columnId = column?.id ?? targetTicket?.columnId;
        if ( !ticket || !columnId )
        {
            return;
        }
        const items = data.tickets.filter( item => item.columnId === columnId && item.id !== ticket.id && !item.archived ).sort( ( left, right ) => left.position - right.position );
        const targetIndex = targetTicket ? items.findIndex( item => item.id === targetTicket.id ) : items.length;
        const movingDown = targetTicket && ticket.columnId === columnId && ticket.position < targetTicket.position;
        const index = targetIndex + ( movingDown ? 1 : 0 );
        const before = items[ index - 1 ]?.position;
        const after = items[ index ]?.position;
        const position = before === undefined ? ( after ?? 2048 ) - 1024 : after === undefined ? before + 1024 : ( before + after ) / 2;
        void onMove( ticket, columnId, position );
    }
    return <DndContext sensors={ sensors } collisionDetection={ args => { const pointerCollisions = pointerWithin( args ); return pointerCollisions.length > 0 ? pointerCollisions : closestCorners( args ); } } onDragStart={ event => { setDragged( data.tickets.find( ticket => ticket.id === event.active.id ) ?? null ); } } onDragCancel={ () => { setDragged( null ); } } onDragEnd={ end }>
        <div className="kanban">{ data.board.columns.map( ( column, index ) => <BoardColumn key={ column.id } column={ column } index={ index } tickets={ tickets.filter( ticket => ticket.columnId === column.id ).sort( ( left, right ) => left.position - right.position ) } data={ data } onOpen={ onOpen } onCreate={ onCreate } /> ) }</div>
        <DragOverlay dropAnimation={ { duration: 180, easing: 'cubic-bezier(0.2, 0, 0, 1)' } }>{ dragged && <div className="drag-overlay"><TicketContent ticket={ dragged } data={ data } /></div> }</DragOverlay>
    </DndContext>;
}

function BoardColumn ( { column, index, tickets, data, onOpen, onCreate }: { column: Column; index: number; tickets: Ticket[]; data: BoardData; onOpen: ( ticket: Ticket ) => void; onCreate: ( column: string ) => void; } )
{
    const { setNodeRef, isOver } = useDroppable( { id: column.id } );
    return <section ref={ setNodeRef } className={ `kanban-column ${ isOver ? 'drop-target' : '' }` }>
        <header className="column-header"><span className={ `column-dot dot-${ index % 5 }` } /><h2>{ column.name }</h2><span className="column-count">{ tickets.length }</span><Tooltip label="Add ticket"><ActionIcon variant="subtle" color="gray" aria-label={ `Add ticket to ${ column.name }` } size="sm" onClick={ () => { onCreate( column.id ); } }><Plus size={ 16 } /></ActionIcon></Tooltip></header>
        <div className="column-body"><SortableContext items={ tickets.map( ticket => ticket.id ) } strategy={ verticalListSortingStrategy }>{ tickets.map( ticket => <SortableTicket key={ ticket.id } ticket={ ticket } data={ data } onOpen={ onOpen } /> ) }</SortableContext>{ tickets.length === 0 && <div className="column-empty">No tickets</div> }<button className="add-ticket" onClick={ () => { onCreate( column.id ); } }><Plus size={ 14 } /> Add ticket</button></div>
    </section>;
}

function SortableTicket ( { ticket, data, onOpen }: { ticket: Ticket; data: BoardData; onOpen: ( ticket: Ticket ) => void; } )
{
    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable( { id: ticket.id } );
    return <article ref={ setNodeRef } style={ { transform: CSS.Transform.toString( transform ), transition, opacity: isDragging ? 0.25 : 1 } } className={ `ticket priority-${ ticket.priority }` }>
        <button className="ticket-open" onClick={ () => { onOpen( ticket ); } } aria-label={ `Open ticket: ${ ticket.title }` }><TicketContent ticket={ ticket } data={ data } /></button>
        <Tooltip label="Move ticket"><button className="drag-handle" aria-label={ `Move ticket: ${ ticket.title }` } { ...attributes } { ...listeners }><GripVertical size={ 14 } /></button></Tooltip>
    </article>;
}

function TicketContent ( { ticket, data }: { ticket: Ticket; data: BoardData; } )
{
    const [ now ] = useState( Date.now );
    const done = ticket.subtasks.filter( task => task.done ).length;
    const assignee = data.members.find( member => member.id === ticket.assigneeId );
    const color = ( { low: 'gray', normal: 'teal', high: 'yellow', urgent: 'red' } as Record<string, string> )[ ticket.priority ];
    const overdue = ticket.dueDate !== null && new Date( ticket.dueDate ).getTime() < now;
    const completed = data.board.columns.find( column => column.id === ticket.columnId )?.isDone;
    return <>{ ticket.images[ 0 ] && <img className="ticket-cover" src={ imageUrl( ticket.images[ 0 ] ) } alt={ ticket.title } loading="lazy" /> }<div className="ticket-content"><div className="ticket-meta"><Group gap={ 4 }>{ ticket.tags.map( value => { const tag = tags.find( tag => tag.value === value ); return <span className="tag-chip" key={ value }><i style={ { background: tag?.color } } />{ tag?.label ?? value }</span>; } ) }</Group>{ completed ? <span className="done-check"><Check size={ 12 } /></span> : ticket.priority !== 'normal' && <Badge size="xs" variant="light" color={ color }>{ ticket.priority }</Badge> }</div><h3 className={ completed ? 'completed-title' : '' }>{ ticket.title }</h3>{ ticket.description && <div className="ticket-excerpt"><Markdown remarkPlugins={ [ remarkGfm ] } skipHtml components={ { a: ( { children } ) => <span>{ children }</span>, img: () => null, input: () => null } }>{ ticket.description }</Markdown></div> }{ ticket.subtasks.length > 0 && <Progress value={ done / ticket.subtasks.length * 100 } color={ done === ticket.subtasks.length ? 'teal' : 'yellow' } size={ 3 } mt={ 12 } mb={ 8 } /> }<div className="ticket-footer"><Group gap={ 9 }>{ ticket.subtasks.length > 0 && <span className="ticket-type"><CheckSquare size={ 12 } />{ done }/{ ticket.subtasks.length }</span> }{ ticket.comments.length > 0 && <span className="ticket-type"><MessageSquare size={ 12 } />{ ticket.comments.length }</span> }{ ticket.dueDate && <span className={ `ticket-type ${ overdue ? 'overdue' : '' }` }><CalendarDays size={ 12 } />{ new Date( ticket.dueDate ).toLocaleDateString( undefined, { month: 'short', day: 'numeric' } ) }</span> }</Group>{ assignee && <span className="ticket-assignee"><Avatar size={ 20 } radius="xl" src={ imageUrl( assignee.avatarId ) }>{ assignee.name.slice( 0, 1 ) }</Avatar>{ assignee.name.split( ' ' )[ 0 ] }</span> }</div></div></>;
}