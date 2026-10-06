import { test, expect } from '@playwright/test';
import { installApiMock } from './fixtures';

for ( const width of [ 1366, 390 ] )
{
    for ( const systemTheme of [ 'light', 'dark' ] as const )
    {
        test( `login theme follows ${ systemTheme } system preference at ${ width }px`, async ( { page }, testInfo ) =>
        {
            await page.setViewportSize( { width, height: 900 } );
            await page.emulateMedia( { colorScheme: systemTheme } );
            const state = await installApiMock( page, false );
            const profileUpdates: string[] = [];
            page.on( 'request', request =>
            {
                const isProfileUpdate = request.method() === 'PUT' && request.url().endsWith( '/auth/profile' );
                if ( isProfileUpdate )
                {
                    profileUpdates.push( request.url() );
                }
            } );
            await page.goto( '/' );
            const root = page.locator( 'html' );
            const oppositeTheme = systemTheme === 'dark' ? 'light' : 'dark';
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', systemTheme );
            await expect( root ).toHaveCSS( '--accent', systemTheme === 'dark' ? '#c99a48' : '#8c641f' );
            await expect( page.getByRole( 'button', { name: 'Sign in', exact: true } ) ).toHaveCSS( 'background-color', 'rgb(140, 100, 31)' );
            await expect( page.getByRole( 'button', { name: systemTheme === 'dark' ? 'Light theme' : 'Dark theme', exact: true } ) ).toBeVisible();
            await page.emulateMedia( { colorScheme: oppositeTheme } );
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', oppositeTheme );
            await expect( root ).toHaveCSS( '--accent', oppositeTheme === 'dark' ? '#c99a48' : '#8c641f' );
            await expect( page.getByRole( 'button', { name: 'Sign in', exact: true } ) ).toHaveCSS( 'background-color', 'rgb(140, 100, 31)' );
            await page.getByRole( 'button', { name: oppositeTheme === 'dark' ? 'Light theme' : 'Dark theme', exact: true } ).click();
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', systemTheme );
            await page.reload();
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', systemTheme );
            await page.getByRole( 'button', { name: systemTheme === 'dark' ? 'Light theme' : 'Dark theme', exact: true } ).click();
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', oppositeTheme );
            expect( profileUpdates ).toHaveLength( 0 );
            await page.screenshot( { path: testInfo.outputPath( 'login-theme.png' ) } );
            expect( await page.locator( '.login-page' ).evaluate( element => element.scrollWidth <= element.clientWidth ) ).toBeTruthy();
            state.account.theme = systemTheme;
            await page.getByLabel( 'Email' ).fill( state.account.email );
            await page.getByRole( 'textbox', { name: /^Password/ } ).fill( 'Test-only-browser-password!' );
            await page.getByRole( 'button', { name: 'Sign in', exact: true } ).click();
            await expect( page.locator( '.app-shell' ) ).toBeVisible();
            await expect( root ).toHaveAttribute( 'data-mantine-color-scheme', systemTheme );
        } );
    }
}

for ( const width of [ 1366, 390 ] )
{
    test( `MCP token board assignments at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        await page.goto( '/' );
        if ( width < 768 )
        {
            await page.getByRole( 'button', { name: 'Expand sidebar', exact: true } ).click();
        }
        await page.getByRole( 'button', { name: 'Profile & settings', exact: true } ).click();
        await page.getByRole( 'tab', { name: 'MCP', exact: true } ).click();
        await page.getByLabel( 'Token name' ).fill( 'Integration' );
        await page.getByRole( 'button', { name: 'Create token', exact: true } ).click();
        await expect( page.getByLabel( 'New token' ) ).toHaveValue( 'test-only-mcp-secret' );
        await expect( page.getByText( 'No board access', { exact: true } ) ).toBeVisible();
        const save = page.getByRole( 'button', { name: 'Save boards for Integration', exact: true } );
        await expect( save ).toBeDisabled();
        await page.getByRole( 'combobox', { name: 'Assigned boards for Integration', exact: true } ).click();
        await page.getByRole( 'option', { name: state.data.board.name, exact: true } ).click();
        await page.getByRole( 'combobox', { name: 'Assigned boards for Integration', exact: true } ).press( 'Escape' );
        const assignment = page.waitForRequest( request => request.method() === 'PUT' && request.url().endsWith( '/boards' ) );
        await save.click();
        expect( ( await assignment ).postDataJSON() ).toEqual( { boardIds: [ state.data.board.id ] } );
        await expect( page.getByText( '1 assigned', { exact: true } ) ).toBeVisible();
        await expect( save ).toBeDisabled();
        await page.reload();
        await page.getByRole( 'button', { name: 'Profile & settings', exact: true } ).click();
        await page.getByRole( 'tab', { name: 'MCP', exact: true } ).click();
        await expect( page.getByText( '1 assigned', { exact: true } ) ).toBeVisible();
        await page.screenshot( { path: testInfo.outputPath( 'mcp-board-assignments.png' ) } );
        expect( await page.locator( '.token-row' ).evaluate( element => element.scrollWidth <= element.clientWidth ) ).toBeTruthy();
        await page.getByRole( 'button', { name: 'Clear boards for Integration', exact: true } ).click();
        const removal = page.waitForRequest( request => request.method() === 'PUT' && request.url().endsWith( '/boards' ) );
        await save.click();
        expect( ( await removal ).postDataJSON() ).toEqual( { boardIds: [] } );
        await expect( page.getByText( 'No board access', { exact: true } ) ).toBeVisible();
        await page.getByRole( 'button', { name: 'Revoke Integration', exact: true } ).click();
        await expect( page.locator( '.token-row' ) ).toHaveCount( 0 );
    } );
}

for ( const width of [ 1366, 390 ] )
{
    test( `profile photo crop and cancellation at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        let uploads = 0;
        let uploadedPhoto: Buffer | null = null;
        await page.route( '**/api/auth/avatar', async route =>
        {
            uploads++;
            const body = route.request().postDataBuffer()!;
            const signature = Buffer.from( [ 137, 80, 78, 71, 13, 10, 26, 10 ] );
            const start = body.indexOf( signature );
            expect( start ).toBeGreaterThan( 0 );
            const end = body.indexOf( Buffer.from( 'IEND' ), start ) + 8;
            uploadedPhoto = body.subarray( start, end );
            state.account.avatarId = 'cropped-avatar';
            await route.fulfill( { json: state.account } );
        } );
        await page.goto( '/' );
        const source = await page.evaluate( () =>
        {
            const canvas = document.createElement( 'canvas' );
            canvas.width = 800;
            canvas.height = 400;
            const context = canvas.getContext( '2d' )!;
            context.fillStyle = '#ff0000';
            context.fillRect( 0, 0, 400, 400 );
            context.fillStyle = '#0000ff';
            context.fillRect( 400, 0, 400, 400 );
            return canvas.toDataURL( 'image/png' ).split( ',' )[ 1 ];
        } );
        const file = { name: 'wide-photo.png', mimeType: 'image/png', buffer: Buffer.from( source, 'base64' ) };
        if ( width < 768 )
        {
            await page.getByRole( 'button', { name: 'Expand sidebar', exact: true } ).click();
        }
        await page.getByRole( 'button', { name: 'Profile & settings', exact: true } ).click();
        const input = page.locator( 'input[type="file"]' );
        await input.setInputFiles( file );
        const editor = page.getByRole( 'dialog', { name: 'Edit photo', exact: true } );
        await expect( editor ).toBeVisible();
        await expect( editor.getByRole( 'button', { name: 'Save photo', exact: true } ) ).toBeEnabled();
        expect( uploads ).toBe( 0 );
        await editor.getByRole( 'button', { name: 'Cancel', exact: true } ).click();
        await expect( editor ).toBeHidden();
        expect( state.account.avatarId ).toBeNull();
        expect( uploads ).toBe( 0 );
        await input.setInputFiles( file );
        const slider = editor.getByRole( 'slider', { name: 'Photo zoom', exact: true } );
        await slider.focus();
        await slider.press( 'End' );
        await expect( slider ).toHaveAttribute( 'aria-valuenow', '3' );
        await editor.getByRole( 'button', { name: 'Reset', exact: true } ).click();
        await expect( slider ).toHaveAttribute( 'aria-valuenow', '1' );
        await slider.focus();
        await slider.press( 'End' );
        const cropper = editor.locator( '.reactEasyCrop_Container' );
        const box = ( await cropper.boundingBox() )!;
        await page.mouse.move( box.x + box.width / 2 + 50, box.y + box.height / 2 );
        await page.mouse.down();
        await page.mouse.move( box.x + box.width / 2 - 100, box.y + box.height / 2, { steps: 12 } );
        await page.mouse.up();
        await expect( slider ).toHaveAttribute( 'aria-valuenow', '3' );
        await expect( editor.getByRole( 'button', { name: 'Save photo', exact: true } ) ).toBeEnabled();
        await page.screenshot( { path: testInfo.outputPath( 'avatar-crop.png' ) } );
        expect( await editor.evaluate( element => element.getBoundingClientRect().right <= window.innerWidth ) ).toBeTruthy();
        await editor.getByRole( 'button', { name: 'Save photo', exact: true } ).click();
        await expect( editor ).toBeHidden();
        expect( uploads ).toBe( 1 );
        expect( state.account.avatarId ).toBe( 'cropped-avatar' );
        const photo = uploadedPhoto! as Buffer;
        expect( photo.readUInt32BE( 16 ) ).toBe( 512 );
        expect( photo.readUInt32BE( 20 ) ).toBe( 512 );
        const centerPixel = await page.evaluate( async base64 =>
        {
            const image = new Image();
            image.src = `data:image/png;base64,${ base64 }`;
            await image.decode();
            const canvas = document.createElement( 'canvas' );
            canvas.width = 512;
            canvas.height = 512;
            const context = canvas.getContext( '2d' )!;
            context.drawImage( image, 0, 0 );
            return Array.from( context.getImageData( 64, 256, 1, 1 ).data );
        }, photo.toString( 'base64' ) );
        expect( centerPixel ).toEqual( [ 0, 0, 255, 255 ] );
    } );
}

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
    await expect( page.locator( '.brand strong' ) ).toHaveText( 'SosôOrganizing your life :D' );
    await expect( page.locator( '.workspace-label' ) ).toHaveText( 'Sosô' );
    await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
    await page.getByRole( 'button', { name: 'Filters', exact: true } ).click();
    await page.getByRole( 'button', { name: 'Bug', exact: true } ).click();
    await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
    await expect( page.locator( '.ticket' ) ).toContainText( 'Fix drag-and-drop glitch' );
    await page.getByRole( 'button', { name: 'Bug', exact: true } ).click();
    await page.getByRole( 'combobox', { name: 'Filter by assignee' } ).click();
    await page.getByRole( 'option', { name: 'Maya Chen' } ).click();
    await expect( page.locator( '.ticket' ) ).toHaveCount( 2 );
    await page.getByRole( 'button', { name: 'Done', exact: true } ).click();
    await page.getByLabel( 'Search tickets' ).fill( 'sprint' );
    await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
} );

test( 'new tickets are assigned to the current user', async ( { page } ) =>
{
    const state = await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'New ticket', exact: true } ).click();
    await page.getByRole( 'textbox', { name: 'Title' } ).fill( 'Write release notes' );
    const creation = page.waitForRequest( request => request.method() === 'POST' && request.url().endsWith( `/boards/${ state.data.board.id }/tickets` ) );
    await page.getByRole( 'button', { name: 'Create ticket', exact: true } ).click();
    expect( ( await creation ).postDataJSON() ).toEqual( { title: 'Write release notes', columnId: 'todo', assigneeId: state.account.id } );
    await expect.poll( () => state.data.tickets.find( ticket => ticket.title === 'Write release notes' )?.assigneeId ).toBe( state.account.id );
} );

test( 'ticket tag names stay in English in the localized editor', async ( { page } ) =>
{
    await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Choose language', exact: true } ).click();
    await page.getByRole( 'button', { name: /Español \(México\)/ } ).click();
    await page.locator( '.ticket' ).filter( { hasText: 'Sketch the board layout' } ).getByRole( 'button', { name: /Abrir tarea: Sketch the board layout/ } ).click();

    const dialog = page.getByRole( 'dialog' );
    await expect( dialog.getByText( 'Etiquetas', { exact: true } ) ).toBeVisible();
    await expect( dialog.locator( '.mantine-MultiSelect-pill' ) ).toHaveText( 'Design' );
    await dialog.getByRole( 'combobox', { name: 'Etiquetas' } ).click();
    await expect( page.getByRole( 'option', { name: 'Bug', exact: true } ) ).toBeVisible();
    await expect( page.getByRole( 'option', { name: 'Feature', exact: true } ) ).toBeVisible();
    await expect( page.getByRole( 'option', { name: 'Error', exact: true } ) ).toHaveCount( 0 );
} );

for ( const width of [ 1366, 390 ] )
{
    test( `ticket search matches IDs at ${ width }px`, async ( { page } ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        const ticket = state.data.tickets[ 2 ];
        ticket.id = 'ab12cd34-ef56-7890-abcd-ef1234567890';
        await page.goto( '/' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
        const search = page.getByRole( 'textbox', { name: 'Search tickets' } );
        for ( const query of [ ticket.id, 'AB12CD34', 'ef56-7890' ] )
        {
            await search.fill( query );
            await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
            await expect( page.locator( '.ticket' ) ).toContainText( ticket.title );
        }
        await search.fill( 'nonexistent-ticket-id' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 0 );
        await search.fill( 'sprint' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
        await expect( page.locator( '.ticket' ) ).toContainText( 'Plan sprint goals' );
        await search.fill( 'Cards sometimes jump' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
        await expect( page.locator( '.ticket' ) ).toContainText( ticket.title );
        await search.fill( '' );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
    } );

    test( `compact board header and filter dialog at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        await installApiMock( page );
        await page.goto( '/' );
        await expect( page.locator( '.board-ticket-count' ) ).toHaveText( '4 tickets' );
        await expect( page.locator( '.topbar-title > .topbar-board-name + .board-ticket-count' ) ).toBeVisible();
        await expect( page.locator( '.board-heading .board-ticket-count' ) ).toHaveCount( 0 );
        await expect( page.locator( '.ticket' ) ).toHaveCount( 4 );
        await expect( page.locator( '.eyebrow, .board-toolbar' ) ).toHaveCount( 0 );
        await expect( page.locator( '.topbar' ).getByRole( 'textbox', { name: 'Search tickets' } ) ).toBeVisible();
        await expect( page.locator( '.board-avatars' ) ).toBeVisible();
        const topbar = ( await page.locator( '.topbar' ).boundingBox() )!;
        const heading = ( await page.locator( '.board-heading' ).boundingBox() )!;
        expect( topbar.height ).toBe( 42 );
        expect( heading.y ).toBe( 42 );
        expect( heading.height ).toBeLessThan( width < 768 ? 110 : 65 );
        for ( const selector of [ '.topbar', '.board-heading' ] )
        {
            expect( await page.locator( selector ).evaluate( element => element.scrollWidth <= element.clientWidth ) ).toBeTruthy();
        }
        await page.screenshot( { path: testInfo.outputPath( 'compact-header.png' ) } );
        await page.getByLabel( 'Search tickets' ).fill( 'glitch' );
        await expect( page.locator( '.board-ticket-count' ) ).toHaveText( '1 ticket' );
        await page.getByLabel( 'Search tickets' ).fill( '' );
        await page.getByRole( 'button', { name: 'Filters', exact: true } ).click();
        const dialog = page.getByRole( 'dialog', { name: 'Filters', exact: true } );
        await dialog.getByRole( 'button', { name: 'Bug', exact: true } ).click();
        await dialog.getByRole( 'combobox', { name: 'Filter by priority' } ).click();
        await page.getByRole( 'option', { name: 'Urgent', exact: true } ).click();
        await page.screenshot( { path: testInfo.outputPath( 'filters.png' ) } );
        await dialog.getByRole( 'button', { name: 'Done', exact: true } ).click();
        await expect( dialog ).toBeHidden();
        await expect( page.locator( '.ticket' ) ).toHaveCount( 1 );
        await page.getByRole( 'button', { name: 'Filters (2)', exact: true } ).click();
        await expect( dialog.getByRole( 'button', { name: 'Bug', exact: true } ) ).toHaveAttribute( 'aria-pressed', 'true' );
        await dialog.getByRole( 'button', { name: 'Clear filters', exact: true } ).click();
        await dialog.getByRole( 'button', { name: 'Done', exact: true } ).click();
        await expect( page.locator( '.board-ticket-count' ) ).toHaveText( '4 tickets' );
        await page.locator( '.board-actions' ).getByRole( 'button', { name: 'Archive', exact: true } ).click();
        await expect( page.getByRole( 'dialog' ) ).toBeVisible();
        await page.keyboard.press( 'Escape' );
        await page.locator( '.board-actions' ).getByRole( 'button', { name: 'New ticket', exact: true } ).click();
        await expect( page.getByRole( 'dialog', { name: 'New ticket', exact: true } ) ).toBeVisible();
    } );
}

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
        await expect( page.locator( '.brand span' ) ).toHaveText( 'Organizing your life :D' );
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
    test( `board card Markdown preview and hover border at ${ width }px`, async ( { page }, testInfo ) =>
    {
        await page.setViewportSize( { width, height: 900 } );
        const state = await installApiMock( page );
        state.data.tickets[ 0 ].description = '**Important** with `code`, ~~obsolete~~ and [Docs](https://example.com).\n\n### Details\n\n- [x] Tested\n- [ ] Pending\n\n| Item | Status |\n| --- | --- |\n| UI | Ready |\n\n<script>window.markdownExecuted = true</script>\n\n![Hidden image](https://example.com/preview.png)\n\n[Unsafe](javascript:alert(1))';
        for ( const theme of [ 'light', 'dark' ] as const )
        {
            state.account.theme = theme;
            await page.goto( '/' );
            const card = page.locator( '.ticket' ).filter( { hasText: 'Sketch the board layout' } );
            const excerpt = card.locator( '.ticket-excerpt' );
            await expect( excerpt.locator( 'strong' ) ).toHaveText( 'Important' );
            await expect( excerpt.locator( 'code' ) ).toHaveText( 'code' );
            await expect( excerpt.locator( 'del' ) ).toHaveText( 'obsolete' );
            await expect( excerpt ).toContainText( 'Docs' );
            await expect( excerpt.locator( 'h3' ) ).toHaveText( 'Details' );
            await expect( excerpt.locator( 'table' ) ).toHaveCount( 1 );
            await expect( excerpt.locator( 'a, input, img, script' ) ).toHaveCount( 0 );
            expect( await excerpt.evaluate( element =>
            {
                const style = getComputedStyle( element );
                return element.clientHeight <= Number.parseFloat( style.lineHeight ) * 2 + 1 && element.scrollHeight > element.clientHeight && element.scrollWidth <= element.clientWidth;
            } ) ).toBeTruthy();
            await card.hover();
            const borderColor = theme === 'dark' ? 'rgb(163, 170, 174)' : 'rgb(116, 123, 138)';
            for ( const side of [ 'top', 'right', 'bottom', 'left' ] )
            {
                await expect( card ).toHaveCSS( `border-${ side }-color`, borderColor );
                await expect( card ).toHaveCSS( `border-${ side }-width`, '1px' );
            }
            await page.screenshot( { path: testInfo.outputPath( `board-card-hover-${ theme }.png` ) } );
            await card.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();
            await expect( page.locator( '.ticket-description-markdown strong' ) ).toHaveText( 'Important' );
        }
    } );

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
    const state = await installApiMock( page );
    const failures: string[] = [];
    page.on( 'pageerror', error => { failures.push( error.message ); } );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();
    await page.getByLabel( 'New subtask', { exact: true } ).fill( 'Review mobile spacing' );
    await page.getByRole( 'button', { name: 'Add subtask', exact: true } ).click();
    await page.getByRole( 'checkbox', { name: 'Complete Review mobile spacing' } ).check();
    await page.getByLabel( 'New comment', { exact: true } ).fill( 'Ready for review' );
    state.data.tickets[ 0 ].description = null as unknown as string;
    await page.getByLabel( 'New comment', { exact: true } ).press( 'Control+Enter' );
    await expect( page.locator( '.comment-markdown' ).getByText( 'Ready for review', { exact: true } ) ).toBeVisible();
    await expect( page.getByRole( 'dialog' ) ).toBeVisible();
    expect( state.data.tickets[ 0 ].comments ).toHaveLength( 1 );
    const activity = page.locator( 'details.ticket-activity' );
    await expect( activity ).toContainText( 'Activity history' );
    await activity.locator( 'summary' ).click();
    await expect( activity ).toContainText( 'added a comment' );
    await expect( activity ).toContainText( 'Maya Chen' );
    expect( failures ).toEqual( [] );
    await page.locator( 'input[type=file]' ).setInputFiles( 'public/logo.jpg' );
    await expect( page.getByAltText( 'Ticket attachment', { exact: true } ) ).toBeVisible();
    await page.getByRole( 'button', { name: 'Save changes' } ).click();
    await expect( page.getByRole( 'dialog' ) ).toHaveCount( 0 );
    await expect( page.locator( '.ticket' ).filter( { hasText: 'Sketch the board layout' } ) ).toContainText( '1/1' );
} );

test( 'ticket edits are automatically saved after ten seconds of inactivity', async ( { page } ) =>
{
    const state = await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();
    await page.getByRole( 'button', { name: 'Edit title', exact: true } ).click();
    await page.clock.install();
    const title = page.getByRole( 'textbox', { name: 'Ticket title' } );
    await title.fill( 'Edited ticket title' );
    await page.clock.runFor( 5_000 );
    expect( state.data.tickets[ 0 ].title ).toBe( 'Sketch the board layout' );
    await title.fill( 'Automatically saved title' );
    await page.clock.runFor( 9_999 );
    expect( state.data.tickets[ 0 ].title ).toBe( 'Sketch the board layout' );
    await page.clock.runFor( 1 );
    await expect.poll( () => state.data.tickets[ 0 ].title ).toBe( 'Automatically saved title' );
} );

test( 'ticket subtasks can be reordered and saved', async ( { page } ) =>
{
    const state = await installApiMock( page );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();

    for ( const title of [ 'First subtask', 'Second subtask' ] )
    {
        await page.getByLabel( 'New subtask', { exact: true } ).fill( title );
        await page.getByRole( 'button', { name: 'Add subtask', exact: true } ).click();
    }

    const moveHandle = page.getByRole( 'button', { name: 'Move subtask: First subtask', exact: true } );
    await moveHandle.focus();
    await page.keyboard.press( 'Space' );
    await page.keyboard.press( 'ArrowDown' );
    await page.keyboard.press( 'Space' );

    const subtaskTitles = page.locator( '.subtask-row input[aria-label="Subtask title"]' );
    await expect( subtaskTitles.nth( 0 ) ).toHaveValue( 'Second subtask' );
    await expect( subtaskTitles.nth( 1 ) ).toHaveValue( 'First subtask' );
    await page.getByRole( 'button', { name: 'Save changes', exact: true } ).click();
    expect( state.data.tickets[ 0 ].subtasks.map( task => task.title ) ).toEqual( [ 'Second subtask', 'First subtask' ] );
} );

test( 'pasting images into ticket descriptions and comments keeps the ticket modal usable', async ( { page } ) =>
{
    const state = await installApiMock( page );
    const failures: string[] = [];
    page.on( 'pageerror', error => { failures.push( error.message ); } );
    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } ).click();

    async function pasteImage ( label: string )
    {
        await page.getByRole( 'textbox', { name: label, exact: true } ).evaluate( ( textarea, fileBytes ) =>
        {
            const transfer = new DataTransfer();
            transfer.items.add( new File( [ new Uint8Array( fileBytes ) ], 'pasted.png', { type: 'image/png' } ) );
            textarea.dispatchEvent( new ClipboardEvent( 'paste', { bubbles: true, clipboardData: transfer } ) );
        }, [ 1, 2, 3 ] );
    }

    await page.getByRole( 'button', { name: 'Edit description', exact: true } ).click();
    await pasteImage( 'Description' );
    await expect.poll( async () => await page.getByRole( 'textbox', { name: 'Description', exact: true } ).inputValue() ).toContain( '/api/images/image-' );
    await pasteImage( 'New comment' );
    await expect.poll( async () => await page.getByRole( 'textbox', { name: 'New comment', exact: true } ).inputValue() ).toContain( '/api/images/image-' );
    await page.getByLabel( 'New comment', { exact: true } ).press( 'Control+Enter' );
    await expect( page.locator( '.comment-markdown .ticket-inline-image img' ) ).toBeVisible();
    await expect( page.getByRole( 'dialog' ) ).toBeVisible();
    expect( state.data.tickets[ 0 ].images ).toHaveLength( 2 );
    expect( failures ).toEqual( [] );
} );

test( 'tickets without descriptions open after switching boards', async ( { page } ) =>
{
    const state = await installApiMock( page );
    const ticket = state.data.tickets[ 0 ];
    Reflect.deleteProperty( ticket, 'description' );
    const otherBoard = {
        ...structuredClone( state.data ),
        board: { ...structuredClone( state.data.board ), id: 'board-other', name: 'Another board' },
        tickets: [],
    };
    const failures: string[] = [];
    page.on( 'pageerror', error => { failures.push( error.message ); } );
    await page.route( '**/api/boards', route => route.fulfill( { json: [ state.data.board, otherBoard.board ] } ) );
    await page.route( '**/api/boards/board-other', route => route.fulfill( { json: otherBoard } ) );

    await page.goto( '/' );
    const openTicket = page.getByRole( 'button', { name: 'Open ticket: Sketch the board layout', exact: true } );
    await openTicket.click();
    await expect( page.getByRole( 'dialog' ) ).toContainText( 'No description' );
    await page.keyboard.press( 'Escape' );

    await page.getByRole( 'button', { name: 'Another board', exact: true } ).click();
    await expect( page.getByRole( 'heading', { name: 'Another board', exact: true } ) ).toBeVisible();
    await page.getByRole( 'button', { name: 'Soso development', exact: true } ).click();
    await expect( page.getByRole( 'heading', { name: 'Soso development', exact: true } ) ).toBeVisible();
    await openTicket.click();
    await expect( page.getByRole( 'dialog' ) ).toContainText( 'No description' );
    expect( failures ).toEqual( [] );
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
    await page.getByRole( 'button', { name: 'Manage account: New teammate' } ).click();
    const boards = page.getByPlaceholder( 'No boards selected' );
    await boards.fill( 'Soso development' );
    await page.getByRole( 'option', { name: 'Soso development' } ).click();
    const update = page.waitForRequest( request => request.method() === 'PUT' && request.url().endsWith( '/admin/accounts/account-10' ) );
    await page.getByRole( 'button', { name: 'Save account' } ).click();
    expect( ( await update ).postDataJSON().boardIds ).toEqual( [ 'board-fixture' ] );
} );

test( 'Dropbox missing write permission shows a clear backup alert', async ( { page } ) =>
{
    await installApiMock( page );
    let savedBackupError: string | null = null;
    await page.route( '**/api/admin/backup**', async route =>
    {
        const request = route.request();
        if ( request.method() === 'GET' )
        {
            return route.fulfill( { json: { connected: true, appKey: 'test-app', lastBackupAt: null, lastError: savedBackupError } } );
        }
        if ( request.method() === 'POST' && new URL( request.url() ).pathname.endsWith( '/run' ) )
        {
            savedBackupError = 'Dropbox is missing the files.content.write permission for this access token. Enable it in the Dropbox App Console, generate a new access token, then replace the saved token in Backup settings.';
            return route.fulfill( { status: 502, json: { detail: savedBackupError } } );
        }
        return route.fulfill( { status: 204 } );
    } );

    await page.goto( '/' );
    await page.getByRole( 'button', { name: 'Backup', exact: true } ).click();
    await page.getByRole( 'button', { name: 'Back up now', exact: true } ).click();

    const alert = page.getByRole( 'alert', { name: 'Dropbox write permission required' } );
    await expect( alert ).toContainText( 'Dropbox App Console' );
    await expect( alert ).toContainText( 'generate a new access token' );
    await expect( alert ).toContainText( 'save Dropbox settings' );
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
        await expect( page.getByText( 'Sketch the board layout', { exact: true } ) ).toBeVisible();
        await page.getByRole( 'button', { name: 'Edit title', exact: true } ).click();
        await expect( page.getByLabel( 'Ticket title', { exact: true } ) ).toBeVisible();
        await page.screenshot( { path: testInfo.outputPath( `${ viewport.name }-ticket.png` ) } );
        expect( failures ).toEqual( [] );
    } );
}
