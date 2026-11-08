# Tasks y carpeta compartida de Google Drive

Desde **0.20.0**, Contacts y Tasks comparten una conexión opcional de Google Drive. HABITAT crea una carpeta en la raíz de Mi unidad:

```text
HABITAT/
├── HABITAT Contacts.csv
└── HABITAT Tasks.json
```

Solo se crea el archivo de un módulo al sincronizarlo. Las revisiones permanecen en el espacio privado de la aplicación; no se crea un archivo visible por tarea ni por actualización. Las programaciones de Codex, mensajes, preferencias y credenciales no se sincronizan.

## Activar en cada PC

1. Abre **Preferences → Google Drive**. También puedes llegar desde **Google Drive sync settings** en Tasks o Contacts.
2. Pulsa **Connect Google Drive** y autoriza la misma cuenta y el mismo cliente OAuth en tus equipos. Una conexión anterior de Contacts se conserva. Si usabas un cliente propio, utiliza **Advanced setup → Choose custom OAuth client…** con ese mismo cliente.
3. Activa **Sync tasks**. Empieza desactivado; **Sync contacts** conserva el comportamiento previo y empieza activado. Ambos interruptores se guardan por equipo y funcionan de forma independiente.
4. Pulsa **Sync now** para comprobar el resultado. **Open HABITAT folder** abre la carpeta verificada de esa cuenta.

Las tareas se guardan primero en este PC. Las altas locales solicitan sincronización tras unos dos segundos; mientras HABITAT permanece abierto consulta cambios aproximadamente cada treinta segundos. Los fallos temporales se reintentan con espera creciente, hasta cinco minutos. Cerrar la aplicación detiene esa actividad. Desactivar un módulo conserva sus archivos locales y su base para reanudarlo. **Disconnect Google Drive** desconecta ambos módulos en ese PC sin borrar sus datos.

El historial del disco utiliza la [compactación local](STORAGE.md#respaldos-y-diagnósticos-del-perfil). La compactación futura en Drive requiere un punto de recuperación verificable que los equipos antiguos puedan reconocer. Antes de retirar cualquier revisión remota se debe probar que un equipo desconectado puede reanudar desde su base anterior y que una interrupción durante la transición conserva la cadena válida.

## Cambios simultáneos y recuperación

Cada tarea conserva su ID, texto original, estado y fecha de creación. Completar y reabrir se propagan entre equipos. Los cambios en tareas distintas y en campos compatibles se combinan. El mismo texto con IDs distintos sigue siendo dos tareas. La ausencia de una tarea en una copia no la elimina: esta versión no incorpora borrado físico.

Si un mismo campo cambia de forma incompatible, la lista local permanece disponible y Preferences muestra ambas versiones. **Keep local task** y **Keep Drive task** resuelven cada tarea por separado; no se publica ninguna decisión hasta resolver todos los conflictos de esa sincronización. Si la lista cambia durante la revisión, se descartan las decisiones anteriores y se solicita revisar de nuevo.

Editar durante una petición de red sigue disponible. Los cambios tardíos se conservan y quedan pendientes; la base de combinación registra únicamente la copia confirmada en Drive. Los errores de Contacts no detienen Tasks y viceversa. La resolución de contactos mantiene sus controles propios descritos en [CONTACTS.md](CONTACTS.md).

Cuando el almacén local recupera tareas de una copia de seguridad, se exige revisar pendientes y completadas antes de pulsar **Sync recovered tasks**. La barrera se mantiene tras reiniciar para evitar subir silenciosamente un posible estado anterior. **Reload tasks** sigue siendo necesario ante un conflicto de escritura local.

## Carpeta y migración de contactos

El CSV existente se mueve a la carpeta con una operación de metadatos: conserva ID, nombre, bytes y referencias a su historial privado. No se vuelve a crear la agenda. El descubrimiento global por propiedades sigue permitiendo que versiones anteriores encuentren el mismo archivo.

HABITAT identifica la carpeta por ID, propietario y propiedades privadas, no solo por su nombre. No toma una carpeta manual ajena llamada HABITAT. Una carpeta renombrada o movida se sigue por su ID. Si la carpeta vinculada desaparece, se pide restaurarla en Google Drive y se conservan los datos locales; no se crea una sustituta vacía.

Si dos conexiones iniciales generan más de una carpeta gestionada, se detiene la sincronización y se muestran enlaces y fechas para elegir **Use this HABITAT folder**. Escoge la misma carpeta en cada PC. Las demás carpetas no se borran automáticamente. Una cuenta o un cliente OAuth diferente se rechaza antes de enviar conjuntos de datos vinculados.

## Datos y límites

| Archivo local | Función |
| --- | --- |
| `tasks.json` y `tasks.json.history` | Lista e historial locales existentes. |
| `tasks.json.sync-recovery` | Barrera persistente de revisión tras recuperación. |
| `drive-workspace.json` | Cuenta, cliente y carpeta verificada o pendiente. |
| `drive-sync-settings.json` | Módulos activados en este PC. |
| `tasks-drive.json` | Identidades, revisión y última copia de tareas confirmada. |
| `contacts-drive.json` | Vínculo y base de contactos existentes. Su `RootId` continúa siendo el historial privado, no la carpeta. |

La conexión reutiliza el almacén de autorización anterior de Contacts. Los tokens no forman parte de los documentos ni del paquete de instalación. Los permisos siguen limitados a `drive.file` y `drive.appdata`. JSON, CSV y revisiones contienen datos legibles, sin cifrado de extremo a extremo; consulta [PRIVACY.md](PRIVACY.md).

Los documentos gestionados son para consulta o descarga. Editarlos manualmente, retirar propiedades o perder su historial detiene la sincronización para revisión; no sustituye la lista local. El JSON de Tasks admite esquema 1, hasta 50.000 tareas y 16 MiB. Un listado de Drive incompleto tampoco se interpreta como un conjunto vacío.

## Verificación

La suite Contacts incluye dos dispositivos con almacenamiento y Drive simulados: altas, completar/reabrir, publicación simultánea, decisiones caducadas, edición durante la red, respuesta perdida, archivo corrupto, errores independientes, pausa/reanudación, migración, carpetas duplicadas o eliminadas, cuenta distinta y recuperación tras reinicio. Las comprobaciones WinUI verifican la nueva categoría y sus interruptores en ambos temas sin usar Google ni datos del usuario. Ejecuta los comandos de [CHECKS.md](CHECKS.md). La prueba con dos equipos físicos y Google real se hace después de activar la opción en ambos equipos.
