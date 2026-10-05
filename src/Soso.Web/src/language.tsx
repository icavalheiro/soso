import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { LanguageContext, type Language } from './language-context';

const translations: Record<string, string> = {
    'Organizing your life :D': 'Organizando tua vida :D', 'Boards': 'Quadros', 'What needs to happen?': 'O que precisa ser feito?',
    'Workspace navigation': 'Navegação do espaço de trabalho', 'Create board': 'Criar quadro', 'BOARDS': 'QUADROS',
    'Accounts': 'Contas', 'Profile & settings': 'Perfil e configurações', 'Open profile': 'Abrir perfil',
    'Administrator': 'Administrador', 'Member': 'Membro', 'Expand sidebar': 'Expandir barra lateral', 'Collapse sidebar': 'Recolher barra lateral',
    'Search tickets': 'Buscar tarefas', 'Search cards...': 'Buscar cartões...', 'Sign out': 'Sair', 'ticket': 'tarefa', 'tickets': 'tarefas', 'Refresh board': 'Atualizar quadro',
    'Your workspace, ready.': 'Seu espaço de trabalho está pronto.', 'Create a board': 'Criar um quadro', 'Filters': 'Filtros', 'Tags': 'Etiquetas',
    'Assignee': 'Responsável', 'Filter by assignee': 'Filtrar por responsável', 'All assignees': 'Todos os responsáveis', 'Unassigned': 'Sem responsável',
    'Priority': 'Prioridade', 'Filter by priority': 'Filtrar por prioridade', 'All priorities': 'Todas as prioridades', 'Urgent': 'Urgente', 'High': 'Alta', 'Normal': 'Normal', 'Low': 'Baixa',
    'Clear filters': 'Limpar filtros', 'Done': 'Concluído', 'Archive': 'Arquivar', 'Archive list': 'Arquivo', 'New ticket': 'Nova tarefa', 'Title': 'Título', 'Column': 'Coluna', 'Create ticket': 'Criar tarefa',
    'Sign in': 'Entrar', 'Email': 'E-mail', 'Password': 'Senha', 'Unable to sign in.': 'Não foi possível entrar.', 'Dark theme': 'Tema escuro', 'Light theme': 'Tema claro',
    'Ticket': 'Tarefa', 'Description': 'Descrição', 'Preview description': 'Visualizar descrição', 'Edit description': 'Editar descrição', 'Add a description': 'Adicione uma descrição', 'No description': 'Sem descrição',
    'Subtasks': 'Subtarefas', 'Complete': 'Concluir', 'Subtask title': 'Título da subtarefa', 'Remove subtask': 'Remover subtarefa', 'New subtask': 'Nova subtarefa', 'Add a subtask': 'Adicionar uma subtarefa', 'Add subtask': 'Adicionar subtarefa',
    'Images': 'Imagens', 'Add image': 'Adicionar imagem', 'View attached image': 'Ver imagem anexada', 'Ticket attachment': 'Anexo da tarefa', 'Remove image': 'Remover imagem',
    'Comments': 'Comentários', 'Former member': 'Ex-membro', 'Delete comment': 'Excluir comentário', 'New comment': 'Novo comentário', 'Write a comment': 'Escreva um comentário', 'Post comment': 'Publicar comentário',
    'Status': 'Status', 'Due date': 'Data de entrega', 'Delete ticket': 'Excluir tarefa', 'Restore': 'Restaurar', 'Save changes': 'Salvar alterações', 'Delete ticket?': 'Excluir tarefa?', 'Discard changes?': 'Descartar alterações?',
    'Cancel': 'Cancelar', 'Delete permanently': 'Excluir permanentemente', 'Discard': 'Descartar', 'Attached image': 'Imagem anexada', 'The ticket, comments and images will be permanently deleted.': 'A tarefa, os comentários e as imagens serão excluídos permanentemente.', 'Your unsaved changes will be lost.': 'Suas alterações não salvas serão perdidas.',
    'Move ticket': 'Mover tarefa', 'Add ticket': 'Adicionar tarefa', 'No tickets': 'Nenhuma tarefa', 'Open ticket': 'Abrir tarefa',
    'Archive ticket': 'Arquivar tarefa', 'Restore ticket': 'Restaurar tarefa', 'Archived': 'Arquivados', 'Search archive': 'Buscar no arquivo', 'No completed tickets': 'Nenhuma tarefa concluída', 'No archived tickets': 'Nenhuma tarefa arquivada',
    'Board settings': 'Configurações do quadro', 'Name': 'Nome', 'Members': 'Membros', 'Columns': 'Colunas', 'Completed column': 'Coluna de concluídos', 'Add column': 'Adicionar coluna', 'New column': 'Nova coluna', 'Delete board': 'Excluir quadro', 'Delete board?': 'Excluir quadro?',
    'All tickets, comments and images in this board will be permanently deleted.': 'Todas as tarefas, comentários e imagens deste quadro serão excluídos permanentemente.', 'Save board': 'Salvar quadro', 'Move up': 'Mover para cima', 'Move down': 'Mover para baixo', 'Remove column': 'Remover coluna',
    'Profile': 'Perfil', 'Security': 'Segurança', 'MCP': 'MCP', 'Change photo': 'Alterar foto', 'Appearance': 'Aparência', 'Light': 'Claro', 'Dark': 'Escuro', 'Custom settings': 'Configurações personalizadas', 'Save profile': 'Salvar perfil',
    'Current password': 'Senha atual', 'New password': 'Nova senha', '14 characters minimum': 'Mínimo de 14 caracteres', 'Changing your password ends all sessions and revokes all MCP tokens.': 'A alteração da senha encerra todas as sessões e revoga todos os tokens MCP.', 'Change password': 'Alterar senha',
    'MCP endpoint': 'Endpoint MCP', 'Token name': 'Nome do token', 'Create token': 'Criar token', 'Copy this token now. It will not be shown again. Send it as Authorization: Bearer <token>.': 'Copie este token agora. Ele não será exibido novamente. Envie-o como Authorization: Bearer <token>.', 'New token': 'Novo token', 'Copied': 'Copiado', 'Copy token': 'Copiar token', 'Assigned boards': 'Quadros atribuídos', 'No boards selected': 'Nenhum quadro selecionado', 'Clear boards': 'Limpar quadros', 'assigned': 'atribuídos', 'No board access': 'Sem acesso a quadros', 'Save boards': 'Salvar quadros', 'Revoke token': 'Revogar token',
    'Account management': 'Gerenciar contas', 'Disabled': 'Desativada', 'Admin': 'Administrador', 'Manage account': 'Gerenciar conta', 'New account': 'Nova conta', 'Initial password': 'Senha inicial', 'System administrator': 'Administrador do sistema',
    'Bug': 'Erro', 'Feature': 'Recurso', 'Design': 'Design', 'Docs': 'Documentação', 'Refactor': 'Refatoração', 'Test': 'Teste', 'Chore': 'Manutenção', 'Research': 'Pesquisa',
    'Language': 'Idioma', 'English': 'English', 'Português (Brasil)': 'Português (Brasil)', 'Choose language': 'Escolher idioma', 'Photo zoom': 'Zoom da foto',
};

const spanishTranslations: Record<string, string> = {
    'Organizing your life :D': 'Organiza tu vida :D', 'Boards': 'Tableros', 'What needs to happen?': '¿Qué hay que hacer?',
    'Workspace navigation': 'Navegación del espacio de trabajo', 'Create board': 'Crear tablero', 'BOARDS': 'TABLEROS',
    'Accounts': 'Cuentas', 'Profile & settings': 'Perfil y configuración', 'Open profile': 'Abrir perfil',
    'Administrator': 'Administrador', 'Member': 'Miembro', 'Expand sidebar': 'Expandir barra lateral', 'Collapse sidebar': 'Contraer barra lateral',
    'Search tickets': 'Buscar tareas', 'Search cards...': 'Buscar tarjetas...', 'Sign out': 'Cerrar sesión', 'ticket': 'tarea', 'tickets': 'tareas', 'Refresh board': 'Actualizar tablero',
    'Your workspace, ready.': 'Tu espacio de trabajo está listo.', 'Create a board': 'Crear un tablero', 'Filters': 'Filtros', 'Tags': 'Etiquetas',
    'Assignee': 'Responsable', 'Filter by assignee': 'Filtrar por responsable', 'All assignees': 'Todos los responsables', 'Unassigned': 'Sin asignar',
    'Priority': 'Prioridad', 'Filter by priority': 'Filtrar por prioridad', 'All priorities': 'Todas las prioridades', 'Urgent': 'Urgente', 'High': 'Alta', 'Normal': 'Normal', 'Low': 'Baja',
    'Clear filters': 'Borrar filtros', 'Done': 'Listo', 'Archive': 'Archivar', 'Archive list': 'Archivo', 'New ticket': 'Nueva tarea', 'Title': 'Título', 'Column': 'Columna', 'Create ticket': 'Crear tarea',
    'Sign in': 'Iniciar sesión', 'Email': 'Correo electrónico', 'Password': 'Contraseña', 'Unable to sign in.': 'No se pudo iniciar sesión.', 'Dark theme': 'Tema oscuro', 'Light theme': 'Tema claro',
    'Ticket': 'Tarea', 'Description': 'Descripción', 'Preview description': 'Vista previa de la descripción', 'Edit description': 'Editar descripción', 'Add a description': 'Agrega una descripción', 'No description': 'Sin descripción',
    'Subtasks': 'Subtareas', 'Complete': 'Completar', 'Subtask title': 'Título de la subtarea', 'Remove subtask': 'Eliminar subtarea', 'New subtask': 'Nueva subtarea', 'Add a subtask': 'Agregar una subtarea', 'Add subtask': 'Agregar subtarea',
    'Images': 'Imágenes', 'Add image': 'Agregar imagen', 'View attached image': 'Ver imagen adjunta', 'Ticket attachment': 'Archivo adjunto de la tarea', 'Remove image': 'Eliminar imagen',
    'Comments': 'Comentarios', 'Former member': 'Exmiembro', 'Delete comment': 'Eliminar comentario', 'New comment': 'Nuevo comentario', 'Write a comment': 'Escribe un comentario', 'Post comment': 'Publicar comentario',
    'Status': 'Estado', 'Due date': 'Fecha de entrega', 'Delete ticket': 'Eliminar tarea', 'Restore': 'Restaurar', 'Save changes': 'Guardar cambios', 'Delete ticket?': '¿Eliminar tarea?', 'Discard changes?': '¿Descartar cambios?',
    'Cancel': 'Cancelar', 'Delete permanently': 'Eliminar permanentemente', 'Discard': 'Descartar', 'Attached image': 'Imagen adjunta', 'The ticket, comments and images will be permanently deleted.': 'La tarea, los comentarios y las imágenes se eliminarán permanentemente.', 'Your unsaved changes will be lost.': 'Se perderán los cambios sin guardar.',
    'Move ticket': 'Mover tarea', 'Add ticket': 'Agregar tarea', 'No tickets': 'No hay tareas', 'Open ticket': 'Abrir tarea',
    'Archive ticket': 'Archivar tarea', 'Restore ticket': 'Restaurar tarea', 'Archived': 'Archivadas', 'Search archive': 'Buscar en el archivo', 'No completed tickets': 'No hay tareas completadas', 'No archived tickets': 'No hay tareas archivadas',
    'Board settings': 'Configuración del tablero', 'Name': 'Nombre', 'Members': 'Miembros', 'Columns': 'Columnas', 'Completed column': 'Columna de tareas completadas', 'Add column': 'Agregar columna', 'New column': 'Nueva columna', 'Delete board': 'Eliminar tablero', 'Delete board?': '¿Eliminar tablero?',
    'All tickets, comments and images in this board will be permanently deleted.': 'Todas las tareas, los comentarios y las imágenes de este tablero se eliminarán permanentemente.', 'Save board': 'Guardar tablero', 'Move up': 'Mover hacia arriba', 'Move down': 'Mover hacia abajo', 'Remove column': 'Eliminar columna',
    'Profile': 'Perfil', 'Security': 'Seguridad', 'MCP': 'MCP', 'Change photo': 'Cambiar foto', 'Appearance': 'Apariencia', 'Light': 'Claro', 'Dark': 'Oscuro', 'Custom settings': 'Configuración personalizada', 'Save profile': 'Guardar perfil',
    'Current password': 'Contraseña actual', 'New password': 'Nueva contraseña', '14 characters minimum': 'Mínimo 14 caracteres', 'Changing your password ends all sessions and revokes all MCP tokens.': 'Al cambiar tu contraseña se cerrarán todas las sesiones y se revocarán todos los tokens de MCP.', 'Change password': 'Cambiar contraseña',
    'MCP endpoint': 'Endpoint de MCP', 'Token name': 'Nombre del token', 'Create token': 'Crear token', 'Copy this token now. It will not be shown again. Send it as Authorization: Bearer <token>.': 'Copia este token ahora. No volverá a mostrarse. Envíalo como Authorization: Bearer <token>.', 'New token': 'Nuevo token', 'Copied': 'Copiado', 'Copy token': 'Copiar token', 'Assigned boards': 'Tableros asignados', 'No boards selected': 'No hay tableros seleccionados', 'Clear boards': 'Quitar tableros', 'assigned': 'asignados', 'No board access': 'Sin acceso a tableros', 'Save boards': 'Guardar tableros', 'Revoke token': 'Revocar token',
    'Account management': 'Administración de cuentas', 'Disabled': 'Deshabilitada', 'Admin': 'Administrador', 'Manage account': 'Administrar cuenta', 'New account': 'Nueva cuenta', 'Initial password': 'Contraseña inicial', 'System administrator': 'Administrador del sistema',
    'Bug': 'Error', 'Feature': 'Funcionalidad', 'Design': 'Diseño', 'Docs': 'Documentación', 'Refactor': 'Refactorización', 'Test': 'Prueba', 'Chore': 'Mantenimiento', 'Research': 'Investigación',
    'Language': 'Idioma', 'English': 'English', 'Português (Brasil)': 'Português (Brasil)', 'Español (México)': 'Español (México)', 'Choose language': 'Elegir idioma', 'Photo zoom': 'Zoom de la foto',
};

export function LanguageProvider ( { children }: { children: ReactNode; } )
{
    const [ language, setLanguageState ] = useState<Language>( () =>
    {
        const saved = localStorage.getItem( 'soso-language' );
        return saved === 'pt-BR' || saved === 'es-MX' ? saved : 'en';
    } );
    useEffect( () => { document.documentElement.lang = language; }, [ language ] );
    function setLanguage ( next: Language )
    {
        localStorage.setItem( 'soso-language', next );
        setLanguageState( next );
        document.documentElement.lang = next;
    }
    const t = ( text: string ) => language === 'pt-BR' ? translations[ text ] ?? text : language === 'es-MX' ? spanishTranslations[ text ] ?? text : text;
    return <LanguageContext.Provider value={ { language, setLanguage, t } }>{ children }</LanguageContext.Provider>;
}
