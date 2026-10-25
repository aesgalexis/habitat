# Versiones y distribución

## Criterio de versiones

HABITAT usa **SemVer: MAJOR.MINOR.PATCH**. `VERSION` es la fuente de la versión del escritorio; `Directory.Build.props` la aplica a los ensamblados .NET, incluido el residente del atajo. No se usa la fecha ni un identificador de compilación como versión de producto.

- **PATCH**: correcciones compatibles y ajustes de documentación o distribución que requieren una nueva publicación.
- **MINOR**: funciones nuevas compatibles o mejoras relevantes de comportamiento.
- **MAJOR**: cambios incompatibles en configuración persistida, instalación o contratos públicos que exigen migración o intervención del usuario.
- Durante **0.x**, las publicaciones siguen siendo de desarrollo. Un cambio incompatible incrementa MINOR, se explica en el changelog y debe incluir su plan de migración; PATCH sigue reservado para correcciones compatibles.

La primera distribución instalable del escritorio es **0.9.1**: recoge el estado funcional de 0.9.0 y corrige su comprobación del changelog en clones Windows con CRLF. `v0.9.0` se conserva como etiqueta histórica sin paquetes publicados. La versión 1.0.0 queda para un estado con compatibilidad y distribución consideradas estables. Una etiqueta publicada es inmutable: si una publicación requiere cambios, se incrementa la versión. Las etiquetas siguen el formato `vMAJOR.MINOR.PATCH` y apuntan a un commit de `main` ya verificado.

La extensión de Chrome (`extension/manifest.json`, actualmente 0.10.3) y la de VS Code (`vscode-extension/package.json`, actualmente 0.2.0) tienen versiones independientes porque se instalan y actualizan por separado. Cada paquete registra las versiones incluidas en `release.json`. La versión del protocolo del puente es un contrato independiente de la versión de producto.

La versión 0.10.0 añade la guía de primera puesta en marcha y utiliza exclusivamente Chrome para los destinos web. La extensión 0.9.0 está enviada a Chrome Web Store como Unlisted; el enlace estará disponible tras la aprobación de Google. Se mantiene la carga manual incluida mientras tanto. Consulta [la guía para otro PC](GETTING_STARTED.md).

## Instalar una publicación

La versión **0.11.0** incorpora el aviso automático de versiones nuevas mediante **Update HABITAT**, los destinos personalizados y la gestión de aplicaciones de Windows.

La versión **0.14.0** añade Mercados, la interfaz en inglés/español, fondos personalizables y cinco ilustraciones. También corrige el cierre causado por márgenes negativos de la barra de ventana al minimizar/restaurar. Incluye los cambios de 0.12.0 a 0.13.1 que aún no tenían paquetes publicados en GitHub.

La versión **0.14.2** restaura el comportamiento normal del ratón: hacer clic en HABITAT vuelve a activarlo después de usar el escritorio u otra aplicación. Se elimina el paso automático de clics que obligaba a recuperar la ventana mediante Alt+Tab.

La versión **0.15.0** incorpora descarga, verificación SHA-256, instalación y reapertura desde el icono de actualización. Conserva el formato EXE/ZIP y los borradores. Las versiones anteriores necesitan una instalación manual de 0.15.0 para disponer de este flujo. Consulta [actualizaciones desde la app](AUTO_UPDATES.md).

La versión **0.15.1** añade [mensajes programados para Codex](CODEX_SCHEDULING.md): selección explícita del chat, fecha y hora, prioridad para chats detenidos por límite, cola persistente y gestión de resultados sin duplicar envíos. También permite probar la actualización directa desde 0.15.0.

La versión **0.15.2** limita el selector y la entrega de mensajes programados a chats creados desde HABITAT. También rechaza las programaciones externas guardadas por versiones anteriores antes de leer o abrir su destino.

La versión **0.16.0** incorpora la personalización de la barra lateral, la reanudación programada de varios chats de Codex detenidos por límite, la lectura del modelo y razonamiento del chat y el desvanecido de los bordes de lectura. También corrige avisos de finalización obsoletos y unifica los colores de los autores.

La versión **0.16.1** corrige la pérdida del chat vinculado al navegar por Codex. Conserva la conversación tras reiniciar, verifica su destino al enviar e incorpora «Nuevo chat» para volver al editor inicial sin borrar la conversación anterior.

La versión **0.16.2** reúne todas las funciones consolidadas de 0.16.1 con Facebook y la extensión Chrome 0.10.3: fondos, programación y persistencia de Codex, Mercados, sidebar y destinos configurables. Conserva el modal de actualización, recupera borradores al reiniciar y mantiene la interfaz en inglés. También permite empaquetar bajo OneDrive conservando la comprobación de enlaces de todos los ancestros. Véase [el registro de recuperación](RECOVERY_2026-10-25.md).

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

## Preparar y publicar

La aplicación consulta la última release pública de GitHub al arrancar y cada seis horas mientras permanece abierta. Solo avisa si su versión estable es superior a la instalada y tiene un EXE o ZIP de Windows ya subido. Al pulsar el icono **Actualizar HABITAT**, se refrescan los metadatos y se descarga el formato correspondiente con su SHA-256. Tras verificarlo, espera las acciones en curso y se cierra para instalar y reabrirse. Las copias de desarrollo ofrecen la página oficial como alternativa. No descarga paquetes sin pulsar el icono. Los fallos de red o de metadatos se registran sin interrumpir y se reintentan tras una hora; un aviso ya encontrado se conserva durante esos fallos.

1. Trabaja en una rama y revisa todos los cambios, incluidos archivos nuevos. No incluyas credenciales, datos de usuario ni salidas de `artifacts` en Git.
2. Actualiza `VERSION`, `CHANGELOG.md`, las instrucciones de instalación del README y, si corresponde, las versiones de las extensiones.
3. Ejecuta `scripts\verify.ps1` (Node.js y SDK .NET 10). Si has cambiado controles, ejecuta también `scripts\check-tasks-ui.ps1`; usa servicios sintéticos y almacenamiento aislado.
4. Publica y lleva el commit verificado a `main`. Las etiquetas de publicación deben salir de esa rama; `codex/autosave` sigue siendo la rama de checkpoints.
5. En un árbol limpio, ejecuta `scripts\package-release.ps1`. Publica WinUI y el residente, comprueba las versiones de los ensamblados, empaqueta ambas extensiones y genera el ZIP y su SHA-256 en `artifacts\packages`.
6. Verifica el paquete real con `tests\package.checks.ps1 -PackagePath artifacts\packages\HABITAT-<version>-win-x64.zip`. Comprueba integridad, metadatos, binarios, extensiones e instalación aislada sin arranque.
7. Para comprobar también el EXE localmente, instala Inno Setup 6.7.1 y ejecuta `scripts\build-installer.ps1`; lee la versión de `VERSION` y exige un árbol limpio. Verifica su ciclo silencioso con `scripts\test-installer.ps1 -InstallerPath artifacts\distribution\<version>\HABITAT-<version>-x64-setup.exe` en una cuenta sin instalación EXE previa.
8. Crea la etiqueta anotada `v<version>` en ese commit y súbela. `.github/workflows/release.yml` vuelve a verificar, empaqueta ambos formatos, comprueba sus instalaciones y adjunta el ZIP, el EXE y sus hashes a una GitHub Release.

Los binarios se guardan como **assets de GitHub Releases del repositorio**, no en el historial de Git. El código del instalador, la receta de compilación y el criterio de versiones sí están versionados. El código fuente de cada versión queda fijado por su etiqueta. `release.json` identifica el commit fuente y las versiones de las extensiones; `build-manifest.json` verifica tamaño y SHA-256 de todos los archivos de la aplicación antes de activar una publicación.

Los checks de CI validan lógica y distribución con datos sintéticos. Las pruebas reales de proveedores web, portapapeles o envío de mensajes son optativas y no forman parte de la publicación automatizada.
