# Extensión de HABITAT en Chrome Web Store

**Registro del 19 de octubre de 2026:** Browser Bridge 0.9.0 se envió a Chrome Web Store con visibilidad **Unlisted** y publicación automática tras aprobación. El estado registrado entonces fue **Pendiente de revisión**, sin disponibilidad de instalación. Su ID es `ennnldolgdbbkgmpjjpkafeajihpimii`. Esta guía no consulta el estado actual de la tienda; no debe interpretarse como una confirmación de que siga pendiente. El manifiesto fuente puede haber avanzado; véase [PROJECT_STATUS.md](PROJECT_STATUS.md).

El [panel del elemento](https://chrome.google.com/webstore/devconsole/a22d2854-1ba1-46a3-966b-37dab27b2f83/ennnldolgdbbkgmpjjpkafeajihpimii/edit/status) muestra el estado de revisión al propietario. `packaging/chrome-store/submission.json` conserva el ID, la versión y el hash exacto del paquete enviado. La captura de confirmación queda en `artifacts/chrome-store/0.9.0/submission-pending-review.jpg`.

Esta versión añade iconos, política de privacidad, consentimiento explícito antes de conectar y un botón de conexión automática. El permiso `activeTab`, redundante con los hosts declarados, se ha retirado. El protocolo de escritorio sigue siendo compatible.

La versión pública del escritorio 0.9.1 conserva su selector de navegador: con esa versión debe elegirse **Google Chrome** en **Preferences → Browser**. La siguiente publicación del escritorio incorporará la corrección que utiliza Chrome directamente.

## Archivos de publicación

La copia local **0.10.7** conserva la lectura de All friends como alternativa cuando Facebook no muestra Contacts en Home y añade mensajería explícita desde HABITAT y presencia online/offline solo a partir de etiquetas visibles explícitas; la ausencia de ese dato se mantiene como desconocida. Se mantienen el widget opcional, los avisos de privacidad, la separación entre permiso, conexión y resultado, y el diagnóstico local por fases. Sus permisos de Facebook se solicitan desde **Enable Facebook contacts**; no se conceden al instalar. Esta versión local no se ha enviado a la tienda. El alcance de la comprobación con Facebook real está en [FACEBOOK.md](FACEBOOK.md).

- `extension/manifest.json`: versión y recursos de la extensión.
- `docs/EXTENSION_PRIVACY.md`: política pública, en inglés y sin datos de usuarios.
- `extension/privacy.html`: copia incluida de esa política.
- `packaging/chrome-store/listing.json`: ficha, propósito, justificaciones de permisos, declaraciones de datos e instrucciones para los revisores.
- `packaging/chrome-store`: imagen promocional y capturas de los controles nativos con contenido de ejemplo aislado.

La política se publica en `https://github.com/aesgalexis/habitat/blob/main/docs/EXTENSION_PRIVACY.md`. La extensión no envía conversaciones al desarrollador ni tiene telemetría; utiliza el puente local y las páginas de los proveedores que solicita el usuario. El salto HTTP/WebSocket de loopback se declara explícitamente y no debe describirse como cifrado.

## Preparar y comprobar

1. Para regenerar la identidad gráfica existente y la política incluida: `python scripts/prepare-chrome-store-assets.py` (Pillow).
2. Ejecuta las suites `extension-consent`, `extension-background`, `extension-search`, `extension-youtube` y `extension-translation` bajo `tests`, con Node.js.
3. `scripts/export-chrome-store-screenshots.ps1` compila el exportador WinUI de pruebas. Solo usa almacenamiento aislado en `artifacts` y contenido de ejemplo; no lee las tareas o conversaciones reales.
4. `scripts/package-chrome-extension.ps1` genera el ZIP de tienda y su SHA-256 en `artifacts/chrome-store/<version>`. Usa un directorio nuevo para repetir el empaquetado de una candidata.
5. `tests/chrome-package.checks.ps1 -PackagePath <zip>` verifica el paquete real, el manifiesto en la raíz, sus recursos, tamaños de iconos, política y ausencia de utilidades de desarrollo o datos locales.

El ZIP contiene una lista explícita de archivos. No incluye las utilidades `connect`, `reload` o `finish`, las capturas, documentos de revisión ni la aplicación de escritorio.

## Enviar

La cuenta del propietario debe haber completado el alta y la cuota única del programa. Usa la ficha y los recursos preparados, declara las prácticas de datos de forma coherente con la política, establece **Unlisted** y envía a revisión. Puede requerirse verificación de cuenta o aceptar acuerdos desde el panel; esos pasos pertenecen al titular de la cuenta.

Después del envío, registra el ID de extensión, URL de tienda, versión, hash y estado real de revisión. **Pending review** no significa publicada. Cuando Google la apruebe, el enlace Unlisted permitirá instalarla; los usuarios deberán aceptar los permisos de Chrome y habilitar la conexión. Chrome gestionará las actualizaciones de la versión de tienda.

Una ID de tienda nueva es distinta de la instalación de desarrollo. Para migrar, instala la versión de tienda y desactiva la extensión descomprimida anterior para evitar dos puentes simultáneos. No distribuyas el paquete de tienda con datos o claves de conexión de un perfil local.
