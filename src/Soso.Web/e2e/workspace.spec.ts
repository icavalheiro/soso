import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

test( 'login, software tags and assignee filters', async ( { page } ) =>
{
    await installApiMock( page, false );
    await page.goto( '/' );
    await expect( page ).toHaveTitle( 'Sosô' );
    await expect( page.getByRole( 'heading', { name: 'Sosô', exact: true } ) ).toBeVisible();
    await expect( page.getByRole( 'img', { name: 'Sosô', exact: true } ) ).toBeVisible();
    await page.getByLabel( 'Email' ).fill( 'maya@example.test' );
    await page.getByRole( 'textbox', { name: /^Password/ } ).fill( 'Test-only-browser-password!' );
    await page.getByRole( 'button', { name: 'Sign in', exact: true } ).click();
    await expect( page.locator( '.brand strong' ) ).toHaveText( 'SosôOrganizando tua vida :D' );
    await expect( page.locator( '.workspace-label' ) ).toHaveText( 'Sosô' );
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

test( 'create and edit board icons persist after reload', async ( { page }, testInfo ) =>
{
    const state = await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Create board', exact: true } ).click();
    await page.getByRole( 'textbox', { name: /^Name/ } ).fill( 'Personal plans' );
    await page.getByRole( 'button', { name: 'Board icon: Travel', exact: true } ).click();
    await expect( page.getByRole( 'button', { name: 'Board icon: Travel', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
    await page.getByRole( 'button', { name: 'Board color: Blue', exact: true } ).click();
    await expect( page.getByRole( 'button', { name: 'Board color: Blue', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
    await page.screenshot( { path: testInfo.outputPath( 'board-icon-picker.png' ) } );
    await page.getByRole( 'button', { name: 'Save board', exact: true } ).click();
    await expect( page.getByRole( 'heading', { name: 'Personal plans', exact: true } ) ).toBeVisible();
    expect( state.data.board.icon ).toBe( 'plane' );
    expect( state.data.board.color ).toBe( 'blue' );
    await expect( page.locator( '.board-nav .active .lucide-plane' ) ).toBeVisible();
    await page.getByRole( 'button', { name: 'Board settings', exact: true } ).click();
    await expect( page.getByRole( 'button', { name: 'Board icon: Travel', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
    await expect( page.getByRole( 'button', { name: 'Board color: Blue', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
    await page.getByRole( 'button', { name: 'Board icon: Goals', exact: true } ).click();
    await page.getByRole( 'button', { name: 'Board color: Pink', exact: true } ).click();
    await page.getByRole( 'button', { name: 'Save board', exact: true } ).click();
    await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );
    expect( state.data.board.icon ).toBe( 'target' );
    expect( state.data.board.color ).toBe( 'pink' );
    await page.reload();
    await expect( page.locator( '.board-nav .active .lucide-target' ) ).toBeVisible();
    await expect( page.locator( '.topbar-title .lucide-target' ) ).toBeVisible();
    await expect( page.locator( '.topbar-title .lucide-target' ) ).toHaveCSS( 'stroke', 'rgb(214, 51, 108)' );
    await expect( page.locator( '.board-nav .active .lucide-target' ) ).toHaveCSS( 'stroke', 'rgb(214, 51, 108)' );
    await page.getByRole( 'button', { name: 'Dark theme', exact: true } ).click();
    await expect( page.locator( '.topbar-title .lucide-target' ) ).toHaveCSS( 'stroke', 'rgb(247, 131, 172)' );
    await expect( page.locator( '.board-nav .active .lucide-target' ) ).toHaveCSS( 'stroke', 'rgb(247, 131, 172)' );
    await page.getByRole( 'button', { name: 'Board settings', exact: true } ).click();
    await expect( page.getByRole( 'button', { name: 'Board color: Pink', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
    await expect( page.getByRole( 'button', { name: 'Board icon: Goals', exact: true } ).locator( 'svg' ) ).toHaveCSS( 'stroke', 'rgb(247, 131, 172)' );
    await page.screenshot( { path: testInfo.outputPath( 'board-color-dark.png' ) } );
} );

for ( const width of [ 1366, 390 ] )
{
    test( `board color palette in both themes at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        await page.goto( '/' );
        const strokes: string[] = [];
        for ( const scheme of [ 'light', 'dark' ] )
        {
            if ( scheme === 'dark' )
            {
                await page.getByRole( 'button', { name: 'Dark theme', exact: true } ).click();
            }
            await page.getByRole( 'button', { name: 'Board settings', exact: true } ).click();
            for ( const color of [ 'Teal', 'Blue', 'Cyan', 'Green', 'Purple', 'Pink', 'Orange', 'Gray' ] )
            {
                const swatch = page.getByRole( 'button', { name: `Board color: ${ color }`, exact: true } );
                await swatch.click();
                await expect( swatch ).toHaveAttribute( 'aria-pressed', 'true' );
                await expect( page.locator( '.board-color-picker [aria-pressed="true"]' ) ).toHaveCount( 1 );
            }
            await page.getByRole( 'button', { name: 'Board color: Orange', exact: true } ).click();
            const stroke = await page.getByRole( 'button', { name: 'Board icon: Columns', exact: true } ).locator( 'svg' ).evaluate( icon => getComputedStyle( icon ).stroke );
            strokes.push( stroke );
            const paletteFits = await page.locator( '.board-color-picker' ).evaluate( palette => palette.scrollWidth <= palette.clientWidth );
            expect( paletteFits ).toBeTruthy();
            await page.mouse.move( 0, 0 );
            await page.screenshot( { path: testInfo.outputPath( `board-palette-${ scheme }.png` ) } );
            await page.getByRole( 'button', { name: 'Save board', exact: true } ).click();
            await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );
            expect( state.data.board.color ).toBe( 'orange' );
            await expect( page.locator( '.topbar-title .lucide-columns-3' ) ).toHaveCSS( 'stroke', stroke );
        }
        expect( strokes[ 0 ] ).not.toBe( strokes[ 1 ] );
    } );

    test( `sidebar collapse and persistence at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        await installApiMock( page );
        await page.goto( '/' );
        if ( width < 768 )
        {
            await expect( page.locator( '.sidebar' ) ).toBeHidden();
            await page.getByRole( 'button', { name: 'Expand sidebar', exact: true } ).click();
        }
        await expect( page.locator( '.brand span' ) ).toHaveText( 'Organizando tua vida :D' );
        await expect( page.locator( '.sidebar' ) ).toBeVisible();
        await page.screenshot( { path: testInfo.outputPath( 'sidebar-expanded.png' ) } );
        await page.getByRole( 'button', { name: 'Collapse sidebar', exact: true } ).click();
        await expect( page.locator( '.sidebar' ) ).toBeHidden();
        await expect( page.getByRole( 'button', { name: 'Expand sidebar', exact: true } ) ).toHaveAttribute( 'aria-expanded', 'false' );
        const workspace = await page.locator( '.workspace' ).boundingBox();
        expect( workspace!.x ).toBe( 0 );
        expect( workspace!.width ).toBe( width );
        await page.screenshot( { path: testInfo.outputPath( 'sidebar-collapsed.png' ) } );
        await page.reload();
        await expect( page.locator( '.sidebar' ) ).toBeHidden();
        await page.getByRole( 'button', { name: 'Expand sidebar', exact: true } ).click();
        await expect( page.locator( '.sidebar' ) ).toBeVisible();
        await page.reload();
        await expect( page.locator( '.sidebar' ) ).toBeVisible();
        expect( await page.evaluate( () => document.documentElement.scrollWidth > window.innerWidth ) ).toBeFalsy();
    } );
}

for ( const preferences of [ undefined, null ] )
{
    const label = preferences === null ? 'null' : 'missing';
    test( `password-rotation and theme change with ${ label } settings`, async ( { page } ) =>
    {
        const state = await installApiMock( page );
        let pageLoads = 0;
        page.on( 'load', () => { pageLoads++; } );
        await page.goto( '/' );
        await page.getByRole( 'button', { name: 'Profile & settings', exact: true } ).click();
        await page.getByRole( 'tab', { name: 'Security', exact: true } ).click();
        await page.getByRole( 'textbox', { name: /^Current password/ } ).fill( 'Test-only-browser-password!' );
        await page.getByRole( 'textbox', { name: /^New password/ } ).fill( 'Changed-test-password-2026!' );
        await page.getByRole( 'button', { name: 'Change password', exact: true } ).click();
        await expect( page.getByRole( 'button', { name: 'Sign in', exact: true } ) ).toBeVisible();

        Reflect.set( state.account, 'settings', preferences );
        await page.getByLabel( 'Email' ).fill( 'maya@example.test' );
        await page.getByRole( 'textbox', { name: /^Password/ } ).fill( 'Changed-test-password-2026!' );
        await page.getByRole( 'button', { name: 'Sign in', exact: true } ).click();
        await page.getByRole( 'button', { name: 'Profile & settings', exact: true } ).click();
        await expect( page.getByLabel( 'Custom settings', { exact: true } ) ).toHaveValue( '' );
        const profileResponse = page.waitForResponse( '**/api/auth/profile' );
        await page.getByRole( 'button', { name: 'Save profile', exact: true } ).click();
        const savedProfile = await profileResponse;
        expect( savedProfile.request().postDataJSON().settings ).toBe( '' );
        expect( savedProfile.status() ).toBe( 200 );
        await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );

        Reflect.set( state.account, 'settings', preferences );
        await page.getByRole( 'button', { name: 'Sign out', exact: true } ).click();
        await page.getByLabel( 'Email' ).fill( 'maya@example.test' );
        await page.getByRole( 'textbox', { name: /^Password/ } ).fill( 'Changed-test-password-2026!' );
        await page.getByRole( 'button', { name: 'Sign in', exact: true } ).click();
        const themeResponse = page.waitForResponse( '**/api/auth/profile' );
        await page.getByRole( 'button', { name: 'Dark theme', exact: true } ).click();
        const savedTheme = await themeResponse;
        expect( savedTheme.request().postDataJSON().settings ).toBe( '' );
        expect( savedTheme.status() ).toBe( 200 );
        await expect( page.locator( 'html' ) ).toHaveAttribute( 'data-mantine-color-scheme', 'dark' );
        await expect( page.getByText( 'The Settings field is required.', { exact: true } ) ).toHaveCount( 0 );
        expect( pageLoads ).toBe( 1 );
    } );
}

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

for ( const width of [ 1366, 390 ] )
{
    test( `ticket description Markdown rendering and source editing at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        const source = '# Release notes\n\n**Important** and ~~obsolete~~ with [Docs](https://example.com).\n\n- [x] Tested\n- [ ] Pending\n\n| Item | Status |\n| --- | --- |\n| UI | Ready |\n\n```ts\nconst ready = true;\n```\n\n<script>window.markdownExecuted = true</script>\n\n[Unsafe](javascript:alert(1))';
        state.data.tickets[ 0 ].description = source;
        await page.goto( '/' );
        const openTicket = page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } );
        await openTicket.click();
        const rendered = page.locator( '.ticket-description-markdown' );
        await expect( rendered.getByRole( 'heading', { name: 'Release notes', exact: true } ) ).toBeVisible();
        await expect( rendered.locator( 'strong' ) ).toHaveText( 'Important' );
        await expect( rendered.locator( 'del' ) ).toHaveText( 'obsolete' );
        await expect( rendered.getByRole( 'link', { name: 'Docs', exact: true } ) ).toHaveAttribute( 'href', 'https://example.com' );
        await expect( rendered.getByRole( 'table' ) ).toBeVisible();
        await expect( rendered.locator( 'pre code' ) ).toContainText( 'const ready = true;' );
        await expect( rendered.getByRole( 'checkbox' ).first() ).toBeChecked();
        await expect( rendered.getByRole( 'checkbox' ).first() ).toBeDisabled();
        await expect( rendered.locator( 'script' ) ).toHaveCount( 0 );
        await expect( rendered.locator( 'a[href^="javascript:"]' ) ).toHaveCount( 0 );
        await expect( page.getByRole( 'textbox', { name: 'Description', exact: true } ) ).toHaveCount( 0 );
        const fits = await rendered.evaluate( element => element.getBoundingClientRect().right <= window.innerWidth );
        expect( fits ).toBeTruthy();
        await page.screenshot( { path: testInfo.outputPath( 'description-markdown.png' ) } );
        await page.getByRole( 'button', { name: 'Edit description', exact: true } ).click();
        const editor = page.getByRole( 'textbox', { name: 'Description', exact: true } );
        await expect( editor ).toHaveValue( source );
        const edited = '## Updated\n\nA **saved** description.';
        await editor.fill( edited );
        await page.getByRole( 'button', { name: 'Preview description', exact: true } ).click();
        await expect( rendered.getByRole( 'heading', { name: 'Updated', exact: true } ) ).toBeVisible();
        await page.getByRole( 'button', { name: 'Edit description', exact: true } ).click();
        await expect( editor ).toHaveValue( edited );
        await page.getByRole( 'button', { name: 'Save changes', exact: true } ).click();
        await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );
        expect( state.data.tickets[ 0 ].description ).toBe( edited );
        await page.reload();
        await openTicket.click();
        await expect( rendered.getByRole( 'heading', { name: 'Updated', exact: true } ) ).toBeVisible();
        await page.getByRole( 'button', { name: 'Edit description', exact: true } ).click();
        await editor.fill( '' );
        await page.getByRole( 'button', { name: 'Preview description', exact: true } ).click();
        await expect( rendered ).toHaveText( 'No description' );
    } );
}

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