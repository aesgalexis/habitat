# Agenda unificada

La agenda de la versión 0.17.2 se completa automáticamente con las fuentes activadas y abiertas en Chrome. **Contacts** abre los datos guardados localmente. **Manage contacts** lleva a **Preferences → Contacts**, con **Sources**, **People** y **Possible duplicates**. Requiere la extensión 0.10.8 o posterior; los límites de lectura se detallan abajo.

## Fuentes y primer uso

El flujo normal ofrece dos interruptores: **Facebook** y **Gmail and Google Contacts**. Se utilizan automáticamente los permisos opcionales ya concedidos a la extensión. Si falta acceso, **Enable … in Chrome** abre una página propia de la extensión donde Chrome solicita el permiso una vez. No hace falta escribir nombres de cuentas, importar JSON de OAuth ni revisar una lista antes de guardar. **Off** detiene nuevas lecturas y descarta las observaciones pendientes, conservando los contactos anteriores.

Con HABITAT y la extensión conectados, se observan las pestañas abiertas aproximadamente cada 30 segundos y al cambiar de página, terminar la carga o cambiar permisos. No se abren, navegan ni desplazan pestañas de proveedores para la lectura automática. Las observaciones actualizan el mismo registro mediante identificadores estables; los resultados sin cambios no escriben una nueva revisión. Cerrar una página detiene su lectura y mantiene la agenda local.

| Fuente | Datos automáticos disponibles |
| --- | --- |
| Facebook | Nombres, fotos y enlaces de las personas cargadas en Contacts o All friends. Se lee el perfil propio como identidad de cuenta solo si está explícitamente identificado en la navegación. De lo contrario, la fuente indica **Chrome (account not identified)**; no se atribuye a una cuenta adivinada. |
| Gmail | Nombres y direcciones de los remitentes/corresponsales cargados en las filas de la vista de correo. No es una exportación de Google Contacts y no se recogen asuntos, extractos ni cuerpos de mensajes. |
| Google Contacts | Nombres, correos, teléfonos e identificadores de las filas de contacto cargadas en una pestaña abierta de `contacts.google.com`. Puede completar los datos de personas observadas en Gmail. |

Gmail y Google Contacts se separan por la dirección de cuenta mostrada en el botón de cuenta de Google; una posición como `/u/0` no sirve como identidad persistente. Si esa dirección no aparece o es ambigua, se espera a poder identificarla. Cada observación está limitada a 200 personas por cuenta; se conservan las personas ya vistas al cargar otras filas. No se garantiza la agenda completa ni compatibilidad con todos los diseños de estas páginas. Las coincidencias entre fuentes siguen requiriendo revisión antes de fusionar.

## Otras opciones de contactos

**Other contact options** conserva vías opcionales para importar archivos o leer una agenda completa por API. Estas sí utilizan vista previa:

- **Connect Google account** autoriza cada cuenta por separado, con OAuth de escritorio, PKCE y callback de bucle local. Solicita únicamente `contacts.readonly` y la identidad de correo de la cuenta; no añade permisos de Gmail. Reutiliza la configuración del cliente de Gmail o de una conexión de contactos anterior, si existe. En una instalación vacía solicita el JSON de un cliente **Desktop app** con **People API** habilitada. Se pueden conectar varias cuentas. Reconectar la misma cuenta conserva su fuente y sus identificadores.
- **Import vCard…** lee exportaciones `.vcf` del móvil, iCloud, Outlook u otras agendas. El usuario elige un nombre de fuente; para actualizar una importación debe reutilizar ese nombre. Con UID se actualiza el mismo registro; sin UID se utiliza una huella conservadora. Si cambian datos de identidad ambiguos, pueden aparecer registros separados y una sugerencia para revisar, en lugar de unirlos sin permiso. Se conservan campos desconocidos y datos originales. No es sincronización directa con el teléfono ni con iCloud.
- **Preview Facebook contacts** utiliza la extensión existente y requiere escribir el nombre de la cuenta que está abierta en Chrome. Ese nombre lo aporta el usuario: la extensión no certifica la identidad de la cuenta. Solo obtiene las filas cargadas en Contacts o All friends, hasta el límite del lector; no garantiza la lista completa de amigos. **Enable Facebook contacts** en el popup concede el permiso opcional. La presencia de una instantánea no se presenta como estado permanente de una persona.
- **People → Create contact** permite crear registros locales de HABITAT.

Solo las importaciones opcionales muestran vista previa y selección antes de **Import selected contacts**. **Cancel import** conserva la agenda previa. Las recargas por API de Google son completas y paginadas, bajo petición mediante **Reload contacts**. Una recarga vacía mantiene los registros existentes. Si una recarga completa no vacía se importa íntegramente, los registros ausentes se conservan como **Not in the latest complete import**. Las observaciones automáticas del navegador siempre son parciales y nunca eliminan registros por ausencia.

Los límites actuales son 16 MB para vCard y 20.000 registros por importación vCard o Google. Las identidades repetidas con contenido incompatible rechazan la importación para evitar pérdida de información.

## Personas, procedencia y fusiones

Una persona agrupa registros de fuente independientes. Cada registro conserva su fuente, cuenta, identificador remoto, valores actuales, datos originales y versiones anteriores cuando cambian. Una recarga del mismo identificador no añade otra persona ni rompe una fusión confirmada. Los datos discrepantes no se sobrescriben entre fuentes. **Use name from…** permite elegir el origen del nombre principal.

Las copias del mismo perfil de Facebook se muestran como una sola persona, aunque procedan de una importación anterior y de la lectura automática o de cuentas diferentes. Se agrupan únicamente enlaces de perfil idénticos o equivalentes en host, mayúsculas y barra final; se conservan todos los registros y sus fuentes. Al cargar una agenda anterior se repara el duplicado y se guarda una copia íntegra en el historial. Los identificadores de conversaciones no se usan como identidad global.

**People → Merge with → Review merge** muestra ambas fichas antes de **Confirm merge**. **Possible duplicates** propone coincidencias de correo o teléfono, nunca solo de nombre. Estas coincidencias requieren confirmación. La normalización de teléfono no deduce países ni elimina extensiones. **Keep separate** recuerda el rechazo, también después de lecturas automáticas. Las sugerencias se limitan a 100 por revisión y omiten identificadores compartidos por más de diez personas.

**Undo last merge** restaura los grupos previos conservando las actualizaciones posteriores de sus fuentes. Las fusiones sucesivas se pueden deshacer en orden inverso. Deshacer una agrupación del mismo perfil de Facebook recuerda la decisión de mantener las copias separadas, para no repetirla al sincronizar o reiniciar. Importar, fusionar, elegir nombre y deshacer solo cambian HABITAT: no escriben en Google, Facebook ni las agendas originales.

## Ficha y canales

Pulsar una tarjeta con un solo canal abre directamente ese canal: Messenger selecciona el destinatario y el editor de HABITAT; correo abre el borrador en Gmail. No aparece un aviso informativo y no se envía ningún mensaje. Si hay varios canales o cuentas remitentes se puede elegir en la ficha. **Contact details**, en el menú contextual de la tarjeta, mantiene accesibles los datos y su procedencia.

El buscador local admite nombre, correo y teléfono, ignora acentos/mayúsculas y busca en toda la agenda. La barra presenta hasta 100 coincidencias a la vez; **Load more contacts** añade otras 100. El tamaño sigue en **Preferences → Sidebar → Contact card size**. Si falta el permiso de Google, **Enable Gmail contacts** abre su concesión en Chrome: es necesario pulsar **Enable access** una vez; después las pestañas abiertas se leen automáticamente.

- **Open email draft** abre Gmail con destinatario, cuenta remitente elegida y texto actual del editor. El borrador original de HABITAT se conserva; hay que revisar y enviar en Gmail. No es envío de correo mediante API.
- **Message in Messenger** utiliza la integración ya existente y la cuenta de Facebook actualmente abierta en Chrome. Elige un destino con indicación, editor centrado y **Send message**; seleccionar el canal no envía nada. La verificación usa el nombre y enlace originales de Facebook, aunque la ficha unificada tenga otro nombre principal. Se exige una extensión conectada con mensajería compatible.
- Un teléfono, una URL o una dirección de mensajería importada no habilitan automáticamente iMessage, WhatsApp u otros servicios. Esos canales requieren una integración compatible; no se anuncian como disponibles.

## Almacenamiento y privacidad

La agenda se guarda en el perfil persistente de escritorio, `%LOCALAPPDATA%\HABITAT\contacts.json`. Las sustituciones conservan versiones anteriores en `contacts.json.history`. La escritura es atómica, con exclusión entre procesos y comprobación de revisión: una sesión antigua no puede sobrescribir cambios más recientes. Un error de lectura o guardado bloquea cambios hasta **Reload contacts**; un archivo dañado no se sustituye por una agenda vacía. Se conservan los originales para recuperación.

Los contactos y sus versiones son archivos locales, sin cifrado específico de HABITAT. No se envían a ChatGPT, Codex ni al desarrollador. Las fuentes automáticas utilizan la sesión abierta de Chrome y permisos opcionales del sitio, sin leer cookies, contraseñas ni endpoints privados. Los permisos se pueden revocar en el popup de la extensión o en Chrome. Las autorizaciones de la API opcional de Google se guardan aparte, por fuente, en Windows Credential Manager; los access tokens permanecen en memoria. **Disconnect** elimina esa autorización local y conserva los contactos importados. Las fotos mostradas de Facebook se solicitan a sus servidores HTTPS. Véanse [privacidad de escritorio](PRIVACY.md) y [privacidad de la extensión](EXTENSION_PRIVACY.md).

## Comprobación

`dotnet run --project tests/Habitat.Contacts.Checks --configuration Release` usa exclusivamente archivos sintéticos bajo `artifacts`, sin cuentas reales. Comprueba observaciones automáticas, permisos y fuentes Off, identidades de cuenta, idempotencia, conservación, UID, Unicode, fusiones/deshacer, sugerencias/rechazos, conflictos y rutas de comunicación. `node tests/extension-browser-contacts.checks.cjs` prueba los lectores con DOM sintético, lectura limitada, pestañas/permisos, cierre, revocación durante una lectura y activación desde Chrome. La suite Bridge comprueba el protocolo automático autenticado, sin datos privados en diagnósticos. `scripts/check-tasks-ui.ps1 -FacebookOnly` comprueba alta automática sin Import, interruptores persistentes, conservación al desactivar, tamaños, temas, scroll, fusiones y borradores con mensajería interceptada. La suite Gmail conserva la comprobación del consentimiento independiente para la API opcional. No se utilizan contactos reales ni se envían mensajes. Consulta [CHECKS.md](CHECKS.md).

Referencias: [Google People API](https://developers.google.com/people/api/rest/v1/people.connections/list), [OAuth de escritorio](https://developers.google.com/identity/protocols/oauth2/native-app) y [vCard RFC 6350](https://www.rfc-editor.org/rfc/rfc6350).
