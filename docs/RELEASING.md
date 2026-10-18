# Versiones y distribución

## Criterio de versiones

HABITAT usa **SemVer: MAJOR.MINOR.PATCH**. `VERSION` es la fuente de la versión del escritorio; `Directory.Build.props` la aplica a los ensamblados .NET, incluido el residente del atajo. No se usa la fecha ni un identificador de compilación como versión de producto.

- **PATCH**: correcciones compatibles y ajustes de documentación o distribución que requieren una nueva publicación.
- **MINOR**: funciones nuevas compatibles o mejoras relevantes de comportamiento.
- **MAJOR**: cambios incompatibles en configuración persistida, instalación o contratos públicos que exigen migración o intervención del usuario.
- Durante **0.x**, las publicaciones siguen siendo de desarrollo. Un cambio incompatible incrementa MINOR, se explica en el changelog y debe incluir su plan de migración; PATCH sigue reservado para correcciones compatibles.

La primera publicación del escritorio es **0.9.0**: recoge el estado funcional actual y deja 1.0.0 para una versión con compatibilidad y distribución consideradas estables. Una etiqueta publicada es inmutable: si un binario requiere cambios, se incrementa la versión. Las etiquetas siguen el formato `vMAJOR.MINOR.PATCH` y apuntan a un commit de `main` ya verificado.

La extensión de Chrome (`extension/manifest.json`, actualmente 0.8.0) y la de VS Code (`vscode-extension/package.json`, actualmente 0.2.0) tienen versiones independientes porque se instalan y actualizan por separado. Cada paquete registra las versiones incluidas en `release.json`. La versión del protocolo del puente es un contrato independiente de la versión de producto.

## Instalar una publicación

La publicación incluye dos alternativas: el instalador `HABITAT-<version>-x64-setup.exe`, con desinstalación registrada en Windows, y el ZIP con su instalador PowerShell. Ambos son autocontenidos, se instalan por usuario y tienen un SHA-256 adjunto. El EXE necesita Windows 10 2004 o posterior y admite instalación silenciosa. Consulta [la preparación para WinGet](WINGET.md).

Usa siempre el mismo formato para actualizar una instalación. Los instaladores rechazan mezclar las disposiciones del EXE y el ZIP en una misma carpeta. Para cambiar de formato, cierra HABITAT y el residente, retira la instalación anterior conservando los datos de usuario y después instala el otro formato.

1. Descarga `HABITAT-<version>-win-x64.zip` y su archivo `.sha256` de [GitHub Releases](https://github.com/aesgalexis/habitat/releases).
2. Comprueba el ZIP con `Get-FileHash .\HABITAT-<version>-win-x64.zip -Algorithm SHA256` y compara el resultado con el archivo `.sha256`.
3. Extrae **todo** el ZIP en una carpeta temporal y ejecuta `Install-HABITAT.cmd` desde tu sesión normal de Windows. No necesita SDK, Git, Node.js ni permisos de administrador.
4. El instalador comprueba el manifiesto, instala en `%LOCALAPPDATA%\Programs\HABITAT`, crea una entrada en el menú Inicio y abre HABITAT. Puedes borrar la carpeta temporal después.
5. Carga la carpeta `extension` de la **instalación**, no la temporal, desde `chrome://extensions`. Para VS Code, instala el `.vsix` incluido en `vscode-extension`.

Se distribuye para Windows x64, con mínimo de plataforma Windows 10 1809 declarado por el proyecto; se recomienda Windows 10 2004 o posterior, o Windows 11. Solo se verifica en el equipo de desarrollo y el runner Windows de CI; no se afirma una certificación de todas esas versiones. El ZIP contiene los runtimes .NET y Windows App SDK. La distribución actual no tiene firma de código ni instalador MSI/MSIX.

Para una ruta alternativa:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-HABITAT.ps1 -InstallDirectory C:\Apps\HABITAT
```

`-NoLaunch -NoShortcuts` permite preparar una instalación sin abrir la aplicación ni crear accesos directos, siempre con sus procesos cerrados. Las comprobaciones usan estos parámetros exclusivamente con carpetas aisladas dentro de `artifacts`. El instalador rechaza rutas superpuestas y enlaces de sistema de archivos, y rechaza la sesión temporal del sandbox.

Para actualizar el ZIP, repite su instalación sobre la misma ruta. Se conserva la publicación anterior y se restaura si falla el arranque. El actualizador espera una respuesta de ChatGPT en curso antes de cerrar su instancia. El EXE utiliza el ciclo de actualización y desinstalación de Inno Setup, sin la recuperación transaccional del ZIP. Las tareas, preferencias y credenciales permanecen en sus ubicaciones de usuario; no se incluyen en los paquetes. Después de una actualización, recarga la extensión de Chrome y actualiza el VSIX si su versión cambió. Evita mantener instalaciones distintas abiertas simultáneamente: la aplicación y el puente local son únicos por sesión.

Para retirar una instalación ZIP, desactiva primero **Preferences… → Global shortcut (Ctrl+H)**, cierra HABITAT y elimina su carpeta y la entrada del menú Inicio. Para retirar el EXE, utiliza la desinstalación registrada en Windows. Los datos de usuario se conservan.

## Preparar y publicar

1. Trabaja en una rama y revisa todos los cambios, incluidos archivos nuevos. No incluyas credenciales, datos de usuario ni salidas de `artifacts` en Git.
2. Actualiza `VERSION`, `CHANGELOG.md`, las instrucciones de instalación del README y, si corresponde, las versiones de las extensiones.
3. Ejecuta `scripts\verify.ps1` (Node.js y SDK .NET 10). Si has cambiado controles, ejecuta también `scripts\check-tasks-ui.ps1`; usa servicios sintéticos y almacenamiento aislado.
4. Publica y lleva el commit verificado a `main`. Las etiquetas de publicación deben salir de esa rama; `codex/autosave` sigue siendo la rama de checkpoints.
5. En un árbol limpio, ejecuta `scripts\package-release.ps1`. Publica WinUI y el residente, comprueba las versiones de los ensamblados, empaqueta ambas extensiones y genera el ZIP y su SHA-256 en `artifacts\packages`.
6. Verifica el paquete real con `tests\package.checks.ps1 -PackagePath artifacts\packages\HABITAT-<version>-win-x64.zip`. Comprueba integridad, metadatos, binarios, extensiones e instalación aislada sin arranque.
7. Para comprobar también el EXE localmente, instala Inno Setup 6.7.1 y ejecuta `scripts\build-installer.ps1`; lee la versión de `VERSION`. Verifica su ciclo silencioso con `scripts\test-installer.ps1 -InstallerPath artifacts\distribution\<version>\HABITAT-<version>-x64-setup.exe` en una cuenta sin instalación EXE previa.
8. Crea la etiqueta anotada `v<version>` en ese commit y súbela. `.github/workflows/release.yml` vuelve a verificar, empaqueta ambos formatos, comprueba sus instalaciones y adjunta el ZIP, el EXE y sus hashes a una GitHub Release.

Los binarios se guardan como **assets de GitHub Releases del repositorio**, no en el historial de Git. El código del instalador, la receta de compilación y el criterio de versiones sí están versionados. El código fuente de cada versión queda fijado por su etiqueta. `release.json` identifica el commit fuente y las versiones de las extensiones; `build-manifest.json` verifica tamaño y SHA-256 de todos los archivos de la aplicación antes de activar una publicación.

Los checks de CI validan lógica y distribución con datos sintéticos. Las pruebas reales de proveedores web, portapapeles o envío de mensajes son optativas y no forman parte de la publicación automatizada.
