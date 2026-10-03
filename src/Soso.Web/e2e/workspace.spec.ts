import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

test( 'login, software tags and assignee filters', async ( { page } ) =>
{
    await installApiMock( page, false );
    await page.goto( '/' );
    await page.getByLabel( 'Email' ).fill( 'maya@example.test' );
    await page.getByRole( 'textbox', { name: /^Password/ } ).fill( 'Test-only-browser-password!' );
    await page.getByRole( 'button', { name: 'Sign in', exact: true } ).click();
    await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
    await page.getByRole( 'button', { name: 'Bug', exact: true } ).click();
    await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
    await expect( page.locator( '.ticket' ) ).toContainText( 'Fix drag-and-drop glitch' );
    await page.getByRole( 'button', { name: 'Bug', exact: true } ).click();
    await page.getByRole( 'combobox', { name: 'Filter by assignee' } ).click();
    await page.getByRole( 'option', { name: 'Maya Chen' } ).click();
    await expect( page.locator( '.ticket' ) ).toHaveCount( 2 );
    await page.getByLabel( 'Search tickets' ).fill( 'sprint' );
    await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
} );

test( 'drag lift animation and cross-column move', async ( { page }, testInfo ) =>
{
    const state = await installApiMock( page );
    await page.goto( '/' );
    const handle = page.getByRole( 'button', { name: 'Move ticket: Plan sprint goals', exact: true } );
    const box = await handle.boundingBox();
    expect( box ).not.toBeNull();
    const target = page.locator( '.kanban-column' ).filter( { has: page.getByRole( 'heading', { name: 'In progress', exact: true } ) } );
    const destination = await target.boundingBox();
    expect( destination ).not.toBeNull();
    await page.mouse.move( box!.x + 8, box!.y + 8 );
    await page.mouse.down();
    await page.mouse.move( box!.x + 30, box!.y + 25, { steps: 5 } );
    await expect( page.locator( '.drag-overlay' ) ).toBeVisible();
    await page.screenshot( { path: testInfo.outputPath( 'dragging.png' ) } );
    await page.mouse.move( destination!.x + 100, destination!.y + destination!.height - 20, { steps: 12 } );
    await page.mouse.up();
    await expect( target ).toContainText( 'Plan sprint goals' );
    expect( state.data.tickets.find( ticket => ticket.title === 'Plan sprint goals' )!.columnId ).toBe( 'progress' );
} );

test( 'ticket subtasks, comments and images', async ( { page } ) =>
{
    await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();
    await page.getByLabel( 'New subtask', { exact: true } ).fill( 'Review mobile spacing' );
    await page.getByRole( 'button', { name: 'Add subtask', exact: true } ).click();
    await page.getByRole( 'checkbox', { name: 'Complete Review mobile spacing' } ).check();
    await page.getByLabel( 'New comment', { exact: true } ).fill( 'Ready for review' );
    await page.getByRole( 'button', { name: 'Post comment' } ).click();
    await expect( page.getByText( 'Ready for review', { exact: true } ) ).toBeVisible();
    await page.locator( 'input[type=file]' ).setInputFiles( 'public/logo.jpg' );
    await expect( page.getByAltText( 'Ticket attachment', { exact: true } ) ).toBeVisible();
    await page.getByRole( 'button', { name: 'Save changes' } ).click();
    await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );
    await expect( page.locator( '.ticket' ).filter( { hasText: 'Sketch the board layout' } ) ).toContainText( '1/1' );
} );

test( 'completed and archived tasks can be archived and restored', async ( { page } ) =>
{
    await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Archive', exact: true } ).click();
    await expect( page.locator( '.archive-row' ) ).toContainText( 'Set up repository' );
    await page.getByRole( 'button', { name: 'Archive ticket', exact: true } ).click();
    await page.getByText( 'Archived', { exact: true } ).click();
    await expect( page.locator( '.archive-row' ) ).toHaveCount( 2 );
    await page.locator( '.archive-row' ).filter( { hasText: 'Set up repository' } ).getByRole( 'button', { name: 'Restore ticket' } ).click();
    await expect( page.locator( '.archive-row' ) ).toHaveCount( 1 );
} );

test( 'administrator account screen', async ( { page } ) =>
{
    await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Accounts', exact: true } ).click();
    await page.getByRole( 'tab', { name: 'New account' } ).click();
    await page.getByLabel( 'Name', { exact: false } ).fill( 'New teammate' );
    await page.getByLabel( 'Email', { exact: false } ).fill( 'teammate@example.test' );
    await page.getByLabel( 'Initial password', { exact: false } ).fill( 'Test-only-browser-password!' );
    await page.getByRole( 'button', { name: 'Create account' } ).click();
    await page.getByRole( 'tab', { name: 'Accounts', exact: true } ).click();
    await expect( page.getByText( 'New teammate', { exact: true } ) ).toBeVisible();
} );

for ( const viewport of [ { name: 'desktop', width: 1366, height: 900 }, { name: 'mobile', width: 390, height: 844 } ] )
{
    test( `${ viewport.name } layout and both themes`, async ( { page }, testInfo ) =>
    {
        const failures: string[] = [];
        page.on( 'pageerror', error => { failures.push( error.message ); } );
        await page.setViewportSize( viewport );
        await installApiMock( page );
        await page.goto( '/' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
        const logoLoaded = await page.locator( '.brand img' ).evaluate( image => ( image as HTMLImageElement ).naturalWidth > 0 );
        expect( logoLoaded ).toBeTruthy();
        const overflow = await page.evaluate( () => document.documentElement.scrollWidth > document.documentElement.clientWidth );
        expect( overflow ).toBeFalsy();
        await page.screenshot( { path: testInfo.outputPath( `${ viewport.name }-light.png` ) } );
        await page.getByRole( 'button', { name: 'Dark theme', exact: true } ).click();
        await expect( page.locator( 'html' ) ).toHaveAttribute( 'data-mantine-color-scheme', 'dark' );
        await page.screenshot( { path: testInfo.outputPath( `${ viewport.name }-dark.png` ) } );
        await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();
        await expect( page.getByLabel( 'Ticket title', { exact: true } ) ).toBeVisible();
        await page.screenshot( { path: testInfo.outputPath( `${ viewport.name }-ticket.png` ) } );
        expect( failures ).toEqual( [] );
    } );
}