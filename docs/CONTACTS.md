# Contactos como destinos

Desde **0.18.0**, HABITAT muestra exclusivamente los contactos que crea el usuario. La nueva agenda empieza con **cero contactos**, aunque una versión anterior hubiera guardado personas de Facebook, Gmail, Google Contacts o archivos. La sincronización opcional con Google Drive comparte las fichas creadas en HABITAT entre equipos.

## Añadir y gestionar

Abre **Contacts → Add contact**. Introduce nombre, teléfono, correo o cualquier combinación. Basta un dato: el nombre es opcional si prefieres identificar el contacto por su teléfono o correo. **Add detail** añade más teléfonos o correos, un **Messenger profile**, organización, web, dirección, cumpleaños, notas u otro dato con etiqueta propia. **Add contact** guarda una sola ficha con todos sus datos. **Cancel** no incorpora nada.

**Manage contacts → Edit contact** permite modificar la ficha. **Save contact** conserva su identidad y sus valores anteriores en el historial local. El mismo teléfono, correo o perfil de Messenger no puede pertenecer a dos fichas; HABITAT pide editar el contacto existente y mantiene los datos del formulario si se rechaza el alta. Se ignoran mayúsculas en el correo y variantes equivalentes de formato en teléfonos y perfiles. No se deducen países ni se fusionan personas solo por compartir nombre. Una segunda ficha idéntica de solo nombre también se rechaza.

**Remove contact → Confirm remove** retira la persona y sus destinos disponibles. **Keep contact** cancela la eliminación. Las copias de recuperación permanecen en el disco. Buscar admite nombre, correo y teléfono, sin distinguir acentos o mayúsculas. La barra carga hasta 100 coincidencias y **Load more contacts** muestra las siguientes.

## Abrir un destino

Un clic en la tarjeta selecciona directamente a esa persona como destino. Mantiene el formato común: indicación de la acción sobre el editor, editor centrado y botón de acción debajo. Seleccionar una persona no envía, no abre otra aplicación y no copia el borrador de otro contacto. Un contacto sin canal compatible también se selecciona, con **Send** deshabilitado.

Los botones de canal aparecen debajo del botón de acción. Solo uno queda seleccionado y determina la vía y el destinatario. Se recuerda el último canal durante la sesión; inicialmente se prioriza teléfono, correo y Messenger. Varios teléfonos o correos se eligen bajo los botones de canal y siguen perteneciendo a una única ficha. Los borradores se separan por persona, canal, dirección y remitente. Renombrar actualiza la cabecera y conserva el borrador; retirar o cambiar el canal activo deja de ofrecer el destino anterior.

- **Email** abre **Email <name>…** y **Draft email**. La integración Gmail guarda un borrador con destinatario fijado. La autorización se pide bajo una acción explícita; guardar el borrador no envía. **Open in browser** permite revisarlo después en Gmail.
- **iMessage** identifica la ruta de teléfono, con **Message <name>…** y **Send**. HABITAT verifica destinatario y texto antes de activar el envío una vez. **Sent via Phone Link.** requiere que Phone Link confirme el envío del mensaje nuevo en esa conversación. Si solo se vacía el mismo editor, HABITAT muestra **Message submitted.** o **iMessage submitted.** según el protocolo disponible. La confirmación se refiere al envío desde Phone Link. Si Phone Link no expone el protocolo, el teléfono podría elegir SMS. Un indicador explícito de SMS bloquea la operación. El botón **SMS** permanece deshabilitado.
- **Facebook** utiliza el perfil de Messenger escrito por el usuario, la extensión y la sesión abierta en Chrome. **Send message** verifica el destinatario antes de enviar. Elegir el canal no envía nada. La presencia es desconocida salvo evidencia disponible.

Tras enviar por Phone Link, HABITAT muestra los mensajes de texto y las respuestas disponibles mientras ese hilo está seleccionado. La lectura empieza al abrir la conversación y se actualiza cada diez segundos. Para consultar una respuesta que ya llegó, haz clic derecho en el contacto y elige **View conversation**. Si tiene varios números, elige el correspondiente. **Clear** vuelve al editor inicial y detiene la lectura del hilo. Los textos de interfaz permanecen en inglés y el contenido de los mensajes conserva su idioma.

El clic derecho también ofrece **Manage contact** en la ficha del contacto.

## Almacenamiento y cambio de modelo

La nueva agenda se guarda en `contact-destinations.json`, dentro del directorio persistente de datos de HABITAT. Sus copias de recuperación quedan en `contact-destinations.json.history`. Los contactos añadidos sobreviven a los reinicios: la lista vacía se aplica al estrenar el nuevo modelo, no se borra la agenda en cada arranque.

`contacts.json` y `contacts.json.history`, utilizados por el modelo anterior de fuentes, permanecen intactos. No se leen para poblar la nueva agenda ni se trasladan automáticamente sus registros. La aplicación fuerza las fuentes de navegador a desactivadas, incluso con permisos y preferencias antiguas; descarta observaciones pendientes y tardías. El código de importación anterior se conserva internamente por compatibilidad, sin controles de conexión o importación en la agenda actual.

Las pruebas usan contactos sintéticos y almacenamiento explícito dentro de `artifacts`; no leen ni modifican la agenda real del usuario. Consulta [CHECKS.md](CHECKS.md).

## Sincronizar con Google Drive

1. Abre **Preferences → Google Drive**, también accesible desde **Contacts → Google Drive sync settings**, y pulsa **Connect Google Drive**.
2. En el navegador, elige tu cuenta de Google y permite el acceso a los archivos de HABITAT.
3. Cuando aparezca **Contacts synced to Google Drive**, repite los pasos en el segundo PC con la misma cuenta. HABITAT encuentra la agenda y sincroniza las fichas. Puedes pulsar **Sync now** para actualizarla al momento.

El acceso normal está disponible en los instaladores que incluyen el cliente OAuth de HABITAT. Si ya vinculaste la agenda con tu propio proyecto, HABITAT conserva ese cliente. En otro PC usa **Advanced setup → Choose custom OAuth client…** con el mismo cliente, o un instalador que lo incluya. Un cliente de otro proyecto abre un espacio de datos distinto. Consulta [cómo preparar el instalador](RELEASING.md#cliente-de-google-drive).

Desde 0.18.2, **Advanced setup** y **Hide advanced setup** despliegan y ocultan esa opción con controles básicos. Se evita el cierre de WinUI al abrir **Preferences → Contacts** con Drive desconectado o pendiente de reconexión. El estado del desplegable se conserva al refrescar la sección.

El CSV se puede consultar o descargar desde Drive. Contiene una fila por detalle con el identificador de ficha, tipo, etiqueta y valor. Los valores que una hoja de cálculo podría interpretar como fórmulas llevan un apóstrofo protector. Haz las altas, ediciones y eliminaciones desde HABITAT. Las revisiones de sincronización se guardan en el espacio privado de la aplicación en el Drive del usuario para conservar cambios simultáneos. El CSV visible refleja el último estado publicado y se actualiza al volver a sincronizar si dos equipos publican a la vez.

Desde 0.20.0 el mismo CSV se traslada a **HABITAT**, una carpeta creada en la raíz de Mi unidad, conservando su ID e historial. **Sync contacts** permite pausarlo sin desconectar la cuenta. **Sync tasks** comparte opcionalmente la lista de tareas mediante un JSON en esa carpeta y empieza desactivado. Consulta [Tasks y carpeta compartida de Google Drive](TASKS_DRIVE_SYNC.md) para configuración, conflictos y recuperación.

Los cambios de fichas distintas se combinan. Si se modifica la misma ficha en ambos PC, **Keep local conflicting contacts** o **Keep Drive conflicting contacts** permite elegir qué versión conservar para las fichas en conflicto. Si dos fichas distintas tienen el mismo teléfono, correo o perfil, revisa los duplicados antes de reintentar. **Disconnect Google Drive** pausa la sincronización en ese PC y conserva la agenda local y el vínculo con esa cuenta para poder reanudarlo.

El CSV es legible y se guarda sin cifrado de extremo a extremo. Google Drive también conserva revisiones privadas con valores anteriores, igual que HABITAT conserva copias de recuperación locales. La app se comunica directamente con Google Drive mediante permisos limitados a sus archivos y a su espacio privado. [Permisos de Google Drive](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)
