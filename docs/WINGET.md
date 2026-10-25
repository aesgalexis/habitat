# Distribución de HABITAT con WinGet

El objetivo es `winget install --exact --id aesgalexis.HABITAT` y, después,
`winget upgrade --exact --id aesgalexis.HABITAT`. **Estos comandos expresan el
objetivo de distribución; su disponibilidad actual no se ha verificado en esta
revisión documental.** El identificador es la propuesta para la primera
solicitud a `microsoft/winget-pkgs`.

El repositorio y la [release 0.9.1](https://github.com/aesgalexis/habitat/releases/tag/v0.9.1)
figuran como públicos en el registro del 18 de octubre de 2026. Ese registro
recoge la solicitud [microsoft/winget-pkgs#455544](https://github.com/microsoft/winget-pkgs/pull/455544)
pendiente de validación y aceptación. Comprueba su estado antes de recomendar la
instalación desde el catálogo; mientras tanto, utiliza los assets de GitHub Releases.

## Preparación incluida

- Licencia MIT en `LICENSE`, elegida por el propietario del proyecto.
- Metadatos compartidos en `packaging/metadata.json`. El editor usa el nombre
  de la cuenta del repositorio: `aesgalexis`.
- Instalador Inno Setup x64, autocontenido tanto para .NET como para Windows
  App SDK, con mínimo Windows 10 2004 (19041). Incluye el componente Ctrl+H.
- Instalación por usuario en `%LOCALAPPDATA%\Programs\HABITAT`, sin solicitar
  permisos de administrador; acceso en Inicio y escritorio opcional.
- Extensión del navegador, VSIX de VS Code e instrucciones en inglés dentro
  de la instalación. Las extensiones se instalan manualmente en sus aplicaciones.
- Instalación silenciosa sin abrir HABITAT, actualización sobre la misma ruta y
  desinstalación sin borrar tareas, preferencias ni credenciales del usuario.
- Generación de los tres manifiestos YAML de WinGet, usando el SHA-256 del
  instalador final y una URL HTTPS que contiene su versión.
- Workflow manual `Package HABITAT for WinGet`: ejecuta las comprobaciones,
  genera el instalador, prueba instalación y desinstalación silenciosas, valida
  los manifiestos con WinGet y guarda los archivos como artefacto de Actions.
  No publica una release ni presenta una solicitud al catálogo.

## Crear el instalador

Se necesita .NET SDK 10 e Inno Setup 6.7.1. Desde PowerShell en el repositorio:

```powershell
$version = (Get-Content VERSION -Raw).Trim()
./scripts/build-installer.ps1 -Version $version
# Si ISCC.exe no está instalado en su ubicación habitual:
./scripts/build-installer.ps1 -Version $version -InnoCompilerPath 'C:\tools\inno\ISCC.exe'
```

La versión se lee de VERSION y debe coincidir con ese archivo. El árbol debe estar limpio y guardado en Git. La primera publicación fue 0.9.1; estos comandos usan la versión actual.
La salida es `artifacts/distribution/<version>/HABITAT-<version>-x64-setup.exe` y su
archivo `.sha256`. La carga verificada queda en un directorio nuevo bajo
`artifacts/packaging`. No se modifica la instalación de desarrollo ni se abre
la aplicación. Para repetir una compilación ya existente, indica un
`-OutputDirectory` nuevo. No reutilices una versión ya publicada para otros bytes.

Antes de distribuir, firma el ejecutable con el certificado de distribución
si está disponible. La generación del manifiesto debe hacerse **después** de
firmar, ya que la firma cambia el hash. El script no firma ni incluye una clave.
Las licencias de los componentes de terceros incluidos siguen siendo aplicables.

## Comprobar instalación y desinstalación

En una cuenta de prueba limpia, Windows Sandbox o un runner de CI:

```powershell
$version = (Get-Content VERSION -Raw).Trim()
./scripts/test-installer.ps1 -InstallerPath "artifacts/distribution/$version/HABITAT-$version-x64-setup.exe"
```

La comprobación instala bajo un directorio único de `artifacts/installer-smoke`,
con registro temporal de desinstalación por usuario, sin accesos directos y sin
abrir HABITAT. Comprueba todos los hashes, los runtimes incluidos y la
correspondencia del registro con el editor y nombre declarados. Repite la
instalación y desinstala en `finally`. Rechaza una cuenta con una instalación
previa registrada. No abre el almacén real de tareas ni inicia la aplicación.
Conserva los logs de instalación, actualización y desinstalación en `artifacts`.

`tests/packaging.checks.ps1` comprueba por separado hashes, rechazo de URLs
inseguras o sin versión y protección contra sobrescritura, con archivos sintéticos.
La suite Hotkey comprueba el cierre del componente con canales aislados; una
desinstalación no detiene el componente de otra instalación del proyecto.

## Publicación pública y envío

La descarga del instalador oficial de 0.9.1 se ha comprobado sin autenticación,
con SHA-256 coincidente, y sus manifiestos públicos pasan `winget validate`.
La instalación, reinstalación y desinstalación silenciosas del EXE se han
verificado localmente y en CI. La prueba local mediante `winget install --manifest`
no se ha ejecutado: `LocalManifestFiles` está deshabilitado y el proceso no tiene
permisos de administrador. Esta limitación está declarada en la solicitud.

Antes de abrir el repositorio se revisaron las ramas y etiquetas remotas,
47 commits, 818 archivos históricos distintos, los diez logs de Actions y el
contenido de los paquetes ZIP y EXE, sin detectar secretos. Los archivos de
datos del usuario y sus credenciales no aparecen en los paquetes. GitHub tiene
activadas la detección de secretos y la protección de subidas.

Para reproducir el proceso en futuras publicaciones:

1. Revisa y prueba la candidata, la identidad del editor y las licencias de
   terceros. Prueba también una actualización desde una versión anterior y
   el arranque en un Windows limpio antes de solicitar el alta.
2. Publica el instalador final en una URL estable con su versión y una página
   pública del producto. No reemplaces el archivo de esa versión.
3. Descarga el EXE de la release pública y genera los manifiestos usando esos bytes reales. El binario de CI puede tener un hash distinto de la compilación local. Para la release correspondiente a VERSION:

```powershell
$version = (Get-Content VERSION -Raw).Trim()
gh release download "v$version" --repo aesgalexis/habitat --pattern '*.exe' --dir "artifacts/public/$version"
./scripts/new-winget-manifest.ps1 -Version $version `
  -InstallerPath "artifacts/public/$version/HABITAT-$version-x64-setup.exe" `
  -InstallerUrl "https://github.com/aesgalexis/habitat/releases/download/v$version/HABITAT-$version-x64-setup.exe" `
  -PackageUrl 'https://github.com/aesgalexis/habitat' `
  -OutputDirectory 'artifacts/winget-public' -VerifyDownload
```

`-VerifyDownload` descarga sin autenticación y compara el SHA-256 antes de
escribir los YAML. Sin esa opción, el resultado es solo un candidato local.
La URL de producto predeterminada apunta al repositorio público de HABITAT.
Usa un directorio de salida nuevo para no sobrescribir candidatos anteriores.

4. Ejecuta `winget validate --manifest <directorio-de-la-version>`.
5. En Windows Sandbox, habilita `LocalManifestFiles` desde una consola
   administrativa y prueba `winget install --manifest <directorio-de-la-version>
   --silent`. Comprueba Inicio, arranque, extensión, actualización y desinstalación.
6. Copia `manifests/a/aesgalexis/HABITAT/<version>` a un fork de
   `microsoft/winget-pkgs` y abre una pull request. Atiende la validación
   automática y la revisión de Microsoft. El comando por identificador estará
   disponible cuando se acepte y publique el manifiesto.

La solicitud inicial se abrió con los tres manifiestos de 0.9.1; su versión vigente se consulta en la pull request.
Su aceptación y la aparición del paquete en el catálogo dependen de Microsoft;
el enlace de descarga directa ya está disponible públicamente.

Referencias: [crear manifiestos](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest),
[presentar el paquete](https://learn.microsoft.com/en-us/windows/package-manager/package/repository),
[validación del catálogo](https://github.com/microsoft/winget-pkgs/blob/master/doc/Validation.md),
[opciones silenciosas de Inno Setup](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).
