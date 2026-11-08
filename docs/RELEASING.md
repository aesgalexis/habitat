# Versiones y distribución

## Criterio de versiones

HABITAT usa **SemVer: MAJOR.MINOR.PATCH**. `VERSION` es la fuente de la versión del escritorio; `Directory.Build.props` la aplica a los ensamblados .NET, incluido el residente del atajo. No se usa la fecha ni un identificador de compilación como versión de producto.

- **PATCH**: correcciones compatibles y ajustes de documentación o distribución que requieren una nueva publicación.
- **MINOR**: funciones nuevas compatibles o mejoras relevantes de comportamiento.
- **MAJOR**: cambios incompatibles en configuración persistida, instalación o contratos públicos que exigen migración o intervención del usuario.
- Durante **0.x**, las publicaciones siguen siendo de desarrollo. Un cambio incompatible incrementa MINOR, se explica en el changelog y debe incluir su plan de migración; PATCH sigue reservado para correcciones compatibles.

La primera distribución instalable del escritorio es **0.9.1**: recoge el estado funcional de 0.9.0 y corrige su comprobación del changelog en clones Windows con CRLF. `v0.9.0` se conserva como etiqueta histórica sin paquetes publicados. La versión 1.0.0 queda para un estado con compatibilidad y distribución consideradas estables. Una etiqueta publicada es inmutable: si una publicación requiere cambios, se incrementa la versión. Las etiquetas siguen el formato `vMAJOR.MINOR.PATCH` y apuntan a un commit de `main` ya verificado.

La extensión de Chrome (`extension/manifest.json`, actualmente 0.10.9) y la de VS Code (`vscode-extension/package.json`, actualmente 0.2.0) tienen versiones independientes porque se instalan y actualizan por separado. Cada paquete registra las versiones incluidas en `release.json`. La versión del protocolo del puente es un contrato independiente de la versión de producto.

La versión 0.10.0 añade la guía de primera puesta en marcha y utiliza exclusivamente Chrome para los destinos web. La extensión 0.9.0 está enviada a Chrome Web Store como Unlisted; el enlace estará disponible tras la aprobación de Google. Se mantiene la carga manual incluida mientras tanto. Consulta [la guía para otro PC](GETTING_STARTED.md).

## Cambio de agenda en 0.18.0

La fuente 0.18.0 incorpora un cambio de modelo: solo los contactos creados por el usuario son destinos. La agenda nueva empieza vacía en `contact-destinations.json`, sin trasladar la agenda anterior; `contacts.json` y su historial permanecen intactos. Las fuentes automáticas y las importaciones se desactivan en el flujo actual. Los contactos creados después del cambio persisten. La sincronización opcional con Google Drive se aplica a esta agenda nueva. Los botones de canal seleccionan una vía y **View conversation** muestra los mensajes y respuestas disponibles en Enlace Móvil.

## Instalar una publicación

La versión **0.18.2** corrige el cierre de Contacts con Drive desconectado y la entrega programada a Codex con varias ventanas. Incorpora invocación del botón Send, motivos concretos de revisión, esperas seguras anteriores a la transferencia y diagnóstico local limitado. No reactiva programaciones antiguas con resultado incierto ni modifica los datos de contactos.

La versión **0.18.3** recupera las lecturas del editor de Codex cuando cambian sus controles de accesibilidad después de escribir. Verifica de nuevo la misma ventana, el chat y el texto exacto, con reintentos limitados y cancelables que nunca repiten la acción de envío.

La versión **0.19.0** permite programar mensajes en cualquier chat local disponible sin archivar, incluidos los iniciados directamente en Codex. El formato 4 conserva esa selección por mensaje sin ampliar las programaciones antiguas. Al volver a 0.18.x se necesita la copia previa de la cola; véase [compatibilidad de la cola](CODEX_SCHEDULING.md#compatibilidad-de-la-cola).

La versión **0.11.0** incorpora el aviso automático de versiones nuevas mediante **Update HABITAT**, los destinos personalizados y la gestión de aplicaciones de Windows.

La versión **0.14.0** añade Mercados, la interfaz en inglés/español, fondos personalizables y cinco ilustraciones. También corrige el cierre causado por márgenes negativos de la barra de ventana al minimizar/restaurar. Incluye los cambios de 0.12.0 a 0.13.1 que aún no tenían paquetes publicados en GitHub.

La versión **0.14.2** restaura el comportamiento normal del ratón: hacer clic en HABITAT vuelve a activarlo después de usar el escritorio u otra aplicación. Se elimina el paso automático de clics que obligaba a recuperar la ventana mediante Alt+Tab.

La versión **0.15.0** incorpora descarga, verificación SHA-256, instalación y reapertura desde el icono de actualización. Conserva el formato EXE/ZIP y los borradores. Las versiones anteriores necesitan una instalación manual de 0.15.0 para disponer de este flujo. Consulta [actualizaciones desde la app](AUTO_UPDATES.md).

La versión **0.15.1** añade [mensajes programados para Codex](CODEX_SCHEDULING.md): selección explícita del chat, fecha y hora, prioridad para chats detenidos por límite, cola persistente y gestión de resultados sin duplicar envíos. También permite probar la actualización directa desde 0.15.0.

La versión **0.15.2** limita el selector y la entrega de mensajes programados a chats creados desde HABITAT. También rechaza las programaciones externas guardadas por versiones anteriores antes de leer o abrir su destino.

La versión **0.16.0** incorpora la personalización de la barra lateral, la reanudación programada de varios chats de Codex detenidos por límite, la lectura del modelo y razonamiento del chat y el desvanecido de los bordes de lectura. También corrige avisos de finalización obsoletos y unifica los colores de los autores.

La versión **0.16.1** corrige la pérdida del chat vinculado al navegar por Codex. Conserva la conversación tras reiniciar, verifica su destino al enviar e incorpora «Nuevo chat» para volver al editor inicial sin borrar la conversación anterior.

La versión **0.16.2** reúne todas las funciones consolidadas de 0.16.1 con Facebook y la extensión Chrome 0.10.3: fondos, programación y persistencia de Codex, Mercados, sidebar y destinos configurables. Conserva el modal de actualización, recupera borradores al reiniciar y mantiene la interfaz en inglés. También permite empaquetar bajo OneDrive conservando la comprobación de enlaces de todos los ancestros. Véase [el registro de recuperación](RECOVERY_2026-10-25.md).

La versión **0.17.1** es la primera distribución instalable con agenda unificada y fuentes automáticas opcionales desde Facebook, Gmail y Google Contacts abiertos, con procedencia y fusiones revisables. También normaliza los separadores ZIP de distribución y conserva diagnósticos del actualizador. Corrige el orden entre el diagnóstico de espera y la señal de preparación del auxiliar. `v0.17.0` se conserva como etiqueta histórica sin paquetes publicados. La extensión incluida es 0.10.8; su lectura se limita a las personas cargadas y nunca envía mensajes por sincronizar contactos.

La versión **0.17.2** repara las copias del mismo perfil de Facebook conservando sus fuentes e historial, abre directamente los contactos con un único canal y ofrece **Enable Gmail contacts** cuando falta el permiso de Google en Chrome. Las coincidencias de correo o teléfono siguen requiriendo revisión.

La versión **0.17.3** mantiene la escritura a contactos de correo dentro de HABITAT, guarda borradores con destinatario fijado mediante la integración de Gmail y excluye direcciones automáticas de tipo no-reply. Elegir el contacto y guardar el borrador no abren Gmail ni envían correo.

La publicación incluye dos alternativas: el instalador `HABITAT-<version>-x64-setup.exe`, con desinstalación registrada en Windows, y el ZIP con su instalador PowerShell. Ambos son autocontenidos, se instalan por usuario y tienen un SHA-256 adjunto. El EXE necesita Windows 10 2004 o posterior y admite instalación silenciosa. Consulta [la preparación para WinGet](WINGET.md).

Usa siempre el mismo formato para actualizar una instalación. Los instaladores rechazan mezclar las disposiciones del EXE y el ZIP en una misma carpeta. Para cambiar de formato, cierra HABITAT y el residente, retira la instalación anterior conservando los datos de usuario y después instala el otro formato.

1. Descarga `HABITAT-<version>-win-x64.zip` y su archivo `.sha256` de [GitHub Releases](https://github.com/aesgalexis/habitat/releases).
2. Comprueba el ZIP con `Get-FileHash .\HABITAT-<version>-win-x64.zip -Algorithm SHA256` y compara el resultado con el archivo `.sha256`.
3. Extrae **todo** el ZIP en una carpeta temporal y ejecuta `Install-HABITAT.cmd` desde tu sesión normal de Windows. No necesita SDK, Git, Node.js ni permisos de administrador.
4. El instalador comprueba el manifiesto, instala en `%LOCALAPPDATA%\Programs\HABITAT`, crea una entrada en el menú Inicio y abre HABITAT. Puedes borrar la carpeta temporal después.
5. Sigue **Set up HABITAT…**, disponible en el primer arranque y en el menú. Abre la tienda de Chrome o, si aún no está disponible, la carpeta `extension` de la **instalación** y `chrome://extensions` para cargarla manualmente. Activa **Enable connection** y abre tu conversación de ChatGPT en Chrome. Para VS Code, instala el `.vsix` incluido en `vscode-extension`.

Se distribuye para Windows x64, con mínimo de plataforma Windows 10 1809 declarado por el proyecto; se recomienda Windows 10 2004 o posterior, o Windows 11. Solo se verifica en el equipo de desarrollo y el runner Windows de CI; no se afirma una certificación de todas esas versiones. El ZIP contiene los runtimes .NET y Windows App SDK. La distribución actual no tiene firma de código ni instalador MSI/MSIX.

Para una ruta alternativa:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-HABITAT.ps1 -InstallDirectory C:\Apps\HABITAT
```

`-NoLaunch -NoShortcuts` permite preparar una instalación sin abrir la aplicación ni crear accesos directos, siempre con sus procesos cerrados. Las comprobaciones usan estos parámetros exclusivamente con carpetas aisladas dentro de `artifacts`. El instalador rechaza rutas superpuestas y enlaces de sistema de archivos, y rechaza la sesión temporal del sandbox.

Para actualizar el ZIP, repite su instalación sobre la misma ruta. Se conserva la publicación anterior y se restaura si falla el arranque. El actualizador espera una respuesta de ChatGPT en curso antes de cerrar su instancia. El EXE utiliza el ciclo de actualización y desinstalación de Inno Setup, sin la recuperación transaccional del ZIP. Las tareas, preferencias y credenciales permanecen en sus ubicaciones de usuario; no se incluyen en los paquetes. Después de una actualización, recarga la extensión de Chrome y actualiza el VSIX si su versión cambió. Evita mantener instalaciones distintas abiertas simultáneamente: la aplicación y el puente local son únicos por sesión.

Para retirar una instalación ZIP, desactiva primero **Preferences… → Global shortcut (Ctrl+H)**, cierra HABITAT y elimina su carpeta y la entrada del menú Inicio. Para retirar el EXE, utiliza la desinstalación registrada en Windows. Los datos de usuario se conservan.

## Cliente de Google Drive

Para que **Connect Google Drive** abra Google directamente, el instalador necesita un cliente OAuth **Desktop app** del proyecto de HABITAT, con Drive API habilitada y pantalla de consentimiento configurada. Si ya hay contactos sincronizados, usa el mismo cliente de escritorio que creó el vínculo o planifica una migración. Drive aísla el historial de cada aplicación.

Configura el JSON del cliente de escritorio destinado a distribución como secreto de GitHub Actions llamado HABITAT_DRIVE_OAUTH_CLIENT_JSON. El workflow lo pasa a la compilación en HabitatDriveOAuthClientFile. Para un paquete privado local, apunta esa variable de entorno a un JSON de escritorio fuera del repositorio. Una compilación sin ese parámetro ofrece la configuración avanzada. El cliente OAuth de escritorio distribuido es público y puede extraerse del instalador. Los tokens de cada usuario se autorizan en el navegador y se guardan en el almacén de credenciales de Windows.

[OAuth para aplicaciones de escritorio](https://developers.google.com/identity/protocols/oauth2/native-app) · [Alcances de Google Drive](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)

## Preparar y publicar

La [política de almacenamiento](STORAGE.md) retira las salidas de trabajo verificadas al finalizar. Las cachés se reutilizan entre compilaciones. Los entregables locales se retiran tras comprobar su publicación.

El flujo de **Update**, sus plazos y la recuperación de cada formato se describen en [Actualizaciones desde HABITAT](UPDATES.md).

1. Revisa los cambios y actualiza `VERSION`, `CHANGELOG.md`, las instrucciones de instalación del README y las versiones de las extensiones que cambien. Guarda en Git el código y las pruebas sintéticas.
2. Integra el commit en `main` y crea la etiqueta anotada `v<version>` sobre ese commit. Si ambos están pendientes de subir, usa `git push --atomic origin main refs/tags/v<version>`. Si el commit ya está en GitHub, sube solo la etiqueta.
3. Sigue la ejecución **Publish HABITAT** hasta que GitHub adjunte el EXE, el ZIP y sus SHA-256. Mientras genera y prueba los paquetes, el workflow busca la verificación del mismo commit en `main`. Reutiliza la ejecución si pasa. Si no encuentra una ejecución utilizable, verifica la etiqueta. Un resultado fallido o una ejecución que excede el presupuesto bloquea la publicación.
4. Revisa el resumen de entrega. La última fase compara el commit etiquetado, el tamaño y el SHA-256 de los cuatro archivos con GitHub antes de retirar las copias locales. La subida y las fases de compilación, verificación y empaquetado registran su duración.

Puedes usar `scripts\verify.ps1` para ejecutar la verificación local completa durante el desarrollo. Para diagnosticar la distribución en una cuenta de pruebas sin instalación EXE previa, `scripts\build-release-assets.ps1 -DriveClientPath <archivo>` construye ambos formatos y prueba sus instalaciones.

Los binarios se guardan como **assets de GitHub Releases del repositorio**, no en el historial de Git. El código del instalador, la receta de compilación y el criterio de versiones sí están versionados. El código fuente de cada versión queda fijado por su etiqueta. `release.json` identifica el commit fuente y las versiones de las extensiones; `build-manifest.json` verifica tamaño y SHA-256 de todos los archivos de la aplicación antes de activar una publicación.

Los checks de CI validan lógica y distribución con datos sintéticos. Las pruebas reales de proveedores web, portapapeles o envío de mensajes son optativas y no forman parte de la publicación automatizada.

## Cerrar una publicación local

[complete-release.ps1](../scripts/complete-release.ps1) consulta la publicación y muestra los archivos elegibles. Añade `-Apply` para retirar las copias que coincidan. Con `-Worktree` se puede cerrar además una copia registrada bajo `artifacts/release-checkouts` si está en el commit publicado y solo contiene código versionado, el bloqueo vacío o enlaces reconocidos a la caché. Si hay cambios locales, archivos ignorados sin clasificar o diferencias de hash, el resumen indica qué se conserva.

```powershell
./scripts/complete-release.ps1 -Tag v0.20.0 -Repository aesgalexis/habitat
```

## Payload común para ZIP y EXE

El workflow oficial genera una sola compilación con `HabitatDriveOAuthClientFile`. Verifica el recurso de Drive y pasa el mismo directorio a los dos empaquetadores mediante `-StageDirectory -RequireDrive`. Los builds personalizados pueden omitir el cliente integrado y usar **Advanced setup**. [Script del ZIP](../scripts/package-release.ps1) · [Script del EXE](../scripts/build-installer.ps1).

Las comprobaciones del ZIP extraído y del EXE instalado usan `-ReferenceDirectory` para comparar los SHA-256 de los binarios y `-RequireDrive` para exigir el recurso de OAuth. El verificador lee los metadatos del ensamblado sin iniciarlo. [Script de verificación](../scripts/check-release-payload.ps1).
