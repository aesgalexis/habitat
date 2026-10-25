# Diagnóstico y recuperación

Esta guía corresponde al comportamiento presente en la copia revisada; consulta [PROJECT_STATUS.md](PROJECT_STATUS.md) si una opción descrita en otra guía no aparece. Conserva cualquier texto importante antes de reiniciar o actualizar.

## La extensión no conecta

1. Abre **Set up HABITAT…** y comprueba qué canal está pendiente: ChatGPT, Google, YouTube o Translate.
2. Comprueba que Chrome está abierto y que la extensión está habilitada en el mismo perfil. En su popup, utiliza **Connect automatically** o **Enable connection**.
3. Para OpenAI, inicia sesión y abre la conversación en Chrome. Si Chrome descartó o congeló la pestaña, ábrela una vez y vuelve a HABITAT.
4. Tras actualizar una extensión cargada manualmente, recárgala en `chrome://extensions`. Evita tener activas simultáneamente las copias de tienda y descomprimida.
5. Si hace falta emparejar manualmente, utiliza **Pairing code…**. El código cambia con el arranque; no lo compartas en incidencias.

Si falta Chrome o la tienda no permite instalar, sigue el recorrido de extensión incluida de [GETTING_STARTED.md](GETTING_STARTED.md). La página de tienda puede no reflejar la versión fuente en desarrollo.

## Un envío falló o no aparece la respuesta

Comprueba primero la conversación original en Chrome o Codex. Un fallo de confirmación no demuestra que el mensaje no llegase. Evita repetirlo hasta comprobar su estado. Las solicitudes en curso pertenecen a su conversación de origen; cambiar de destino no las mueve a otra.

En Codex, **Reload chat** vuelve a leer el historial y **Open in Codex** permite revisar herramientas o solicitudes de aprobación. Conserva los borradores escritos directamente en Codex y espera a que finalice un turno activo. Si hay títulos duplicados, utiliza títulos distintos para que HABITAT pueda identificar el destino. **Stop waiting** solo detiene la espera en HABITAT.

## Google, YouTube o Translate requieren una acción

Abre la página del proveedor en Chrome y completa el consentimiento o la verificación que muestre. Después usa **Retry** o vuelve a ejecutar la acción en HABITAT. Comprueba también el canal correspondiente en la guía de conexión; una conexión de ChatGPT correcta no demuestra que los otros canales estén listos.

Si el sitio cambió su interfaz, incluye el proveedor y el error exacto en la incidencia. Los adaptadores dependen de su DOM; no cambies permisos globales del navegador como solución genérica.

## Gmail no autoriza o solicita conectar de nuevo

Comprueba el cliente OAuth **Desktop app**, Gmail API y el usuario de prueba en tu proyecto de Google. Para `403: access_denied`, sigue la sección específica de [GMAIL_SETUP.md](GMAIL_SETUP.md). La lectura y la creación de borradores tienen autorizaciones diferentes; utiliza **Authorize Gmail drafts** cuando la app lo solicite.

No borres credenciales ni revocas accesos como primer paso de diagnóstico. Consulta la guía antes de desconectar. Nunca adjuntes el JSON OAuth, tokens o contenido de correos a una incidencia.

## Una actualización falla

Reintenta desde **HABITAT menu → Update…**. Una descarga cancelada o fallida deja HABITAT abierto. Un fallo después del cierre intenta reabrir la instalación existente. Para instalar manualmente, descarga el paquete oficial, verifica su hash y conserva el formato EXE o ZIP de tu instalación.

Los diagnósticos están bajo `%LOCALAPPDATA%\HABITAT\updates`: `operations.log`, `last-result.json` y la carpeta `update-<id>` correspondiente. Revisa el contenido antes de compartirlo. Consulta [UPDATES.md](UPDATES.md) para las garantías y límites de recuperación; el EXE y el ZIP no ofrecen la misma recuperación transaccional. No borres selectores o versiones anteriores para forzar una actualización.

## Ctrl+H no captura una selección

Comprueba **Preferences… → Shortcuts**. Si la selección o el respaldo del portapapeles no pueden verificarse, HABITAT puede limitarse a activar su ventana. Prueba copiar y pegar manualmente; el atajo no envía texto automáticamente. Consulta [GLOBAL_SHORTCUT.md](GLOBAL_SHORTCUT.md) para los límites de captura. No ejecutes pruebas sobre el portapapeles real como diagnóstico automático.

## Preparar una incidencia

Incluye versión instalada, versión de Windows, formato EXE/ZIP/desarrollo, destino afectado, pasos mínimos, resultado esperado y error exacto. Añade las versiones de las extensiones y los estados de conexión si son relevantes. Separa un problema de la release instalada de uno observado en una compilación local.

Utiliza [las incidencias del repositorio](https://github.com/aesgalexis/habitat/issues). Excluye conversaciones, tareas, correos, códigos de emparejamiento y credenciales. Comparte únicamente el fragmento de diagnóstico necesario y revisado.
