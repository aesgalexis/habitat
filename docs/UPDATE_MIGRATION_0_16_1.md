# Recuperación del actualizador de desarrollo 0.16.1

Investigación del 27 de octubre de 2026. Destino informado: `C:\proyectos\habitat`,
con `artifacts/current-release.txt` apuntando a
`releases\release-e464b7ac0eb54f75a0b9e0e57c50197f` y ProductVersion
`0.16.1+4faa5e98ad8dc9b291621930d9fd92895f03fba5`.

## Flujo identificado y límite de identidad

El comportamiento informado utiliza `DesktopUpdateService`, `ReleaseUpdateClient`
y una copia de `updater/install-release-update.ps1` de la publicación activa.
El servicio descarga, comprueba SHA-256, extrae y verifica el manifiesto **antes**
de copiar ese auxiliar. El auxiliar confirma preparación, espera al proceso padre,
activa la publicación ZIP y comprueba el arranque; restaura la anterior si falla.

La etiqueta oficial `v0.16.1` no contiene `DesktopUpdateService`. El commit del
ProductVersion identifica una base de compilación con cambios locales, no todos
los bytes del binario afectado. Las pruebas fijan el cliente y los tres auxiliares
históricos de `e5a2ecc`, que implementan el flujo descrito. Sin hashes o una copia
de las DLL del PC afectado sigue pendiente demostrar igualdad binaria exacta.

`AppUpdateInstall.CreateHelperStartInfo` es la factoría de lanzamiento compartida
en el código actual. El antiguo `AppUpdateInstall.Prepare` y su `UpdateHelper.ps1`
embebido corresponden al otro mecanismo: no son el auxiliar que copia el
`DesktopUpdateService` informado.

## Fallo reproducido

El ZIP **oficial e inmutable** de `v0.16.4` contiene entradas con separadores `\`,
generadas por `ZipFile.CreateFromDirectory` bajo Windows PowerShell/.NET Framework.
El cliente histórico rechaza cualquier `\` en `ChildPath`.

Con el ZIP oficial verificado y con un paquete sintético equivalente se reproduce:
descarga al 100 %, SHA-256 correcto y `InvalidDataException` con
`The update contains an invalid path.` durante la extracción. No se llega al
auxiliar ni al cierre o activación. Esto explica la ausencia de un `update-error.log`
del auxiliar en ese caso; además el cliente antiguo elimina su carpeta temporal.

El mismo cliente sí prepara un ZIP equivalente con `/`. Con el auxiliar histórico
sin modificar se comprueban el cierre del padre, activación, arranque autenticado
por identidad y restauración de la publicación anterior ante un fallo simulado de
arranque. Se preservan fuentes, tareas, preferencias y credenciales **sintéticas**.
Las peticiones de salud se redirigen en el envoltorio de prueba a un puerto privado;
no se ocupa el puente ni se inicia la aplicación real del usuario.

La hipótesis `Get-FileHash` se mantiene separada. En este equipo, asignar una ruta
de módulos heredada incompatible no impidió localizarlo: el auxiliar antiguo llegó
a preparación. El arreglo de `PSModulePath` sí está incluido en la 0.16.4 publicada,
pero esa comprobación no demuestra que fuera la causa del fallo remoto.

## Corrección en código

- El lector normaliza `\` a `/` antes de aplicar las mismas restricciones de ruta,
  contención, enlaces, tamaño y duplicados; rechaza también alias con separadores
  mezclados, UNC, rutas absolutas y recorridos `..`.
- La distribución genera nombres ZIP con `/` explícitamente, también con Windows
  PowerShell 5.1. Esto permite a clientes antiguos leer los próximos paquetes sin
  depender de un lector nuevo que todavía no puedan instalar.
- Los fallos de preparación quedan en `updates/diagnostics/update-<id>.jsonl`, fuera
  de las descargas descartables. Incluyen fase, tipo de excepción, HRESULT y salida
  acotada del auxiliar y su código de salida. Se redactan rutas, URLs, correos y
  valores de credenciales. El auxiliar registra activación, arranque y restauración
  después del cierre del padre.

Estos cambios son posteriores a la publicación 0.16.4. No se sustituyen sus assets
ni se afirma que esa publicación ya contenga la corrección del lector ZIP.

## Recuperación exacta del PC afectado

Guarda los borradores en memoria y cierra HABITAT. Copia a una misma carpeta:

1. [recover-development-zip.ps1](../scripts/recover-development-zip.ps1).
2. [recover-development-zip.cmd](../scripts/recover-development-zip.cmd).
3. El [ZIP oficial 0.16.4](https://github.com/aesgalexis/habitat/releases/download/v0.16.4/HABITAT-0.16.4-win-x64.zip)
   y su [SHA-256](https://github.com/aesgalexis/habitat/releases/download/v0.16.4/HABITAT-0.16.4-win-x64.zip.sha256).

Ejecuta `recover-development-zip.cmd` desde la sesión normal de Windows. Equivale a:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\recover-development-zip.ps1 -InstallRoot "C:\proyectos\habitat" -PackagePath ".\HABITAT-0.16.4-win-x64.zip"
```

El script exige el puntero anterior informado y el SHA-256 exacto del asset oficial:
`FD71A3F1349FB9C41119E27DE9D4EEE79D0FABBC7DE0C98B3CE3E7918A98390B`.
Valida extracción, versión, commit y manifiesto; utiliza los módulos verificados
del paquete para preparar otra publicación **solo dentro de artifacts**. Conserva
el formato ZIP, el directorio de instalación y la publicación anterior. No copia
archivos sobre `src`, `scripts`, `docs`, `.git` ni los datos persistentes. Comprueba
el arranque y restaura el puntero si falla. El resultado queda en
`artifacts/recovery/<id>/result.json`.
Antes de activar guarda también `before.json`, con ProductVersion y hashes de las
DLL, el ejecutable y el auxiliar originales; permite identificar después los bytes
del PC afectado sin recoger código fuente ni contenido de usuario.

No ejecutes el instalador genérico `Install-HABITAT.cmd` del ZIP sobre este checkout:
su copia de archivos de raíz es adecuada para una instalación distribuida, pero
puede reemplazar código/documentación del proyecto. La recuperación específica
evita ese paso.

Al abrir, comprueba **About HABITAT… → 0.16.4**. El paso manual es necesario porque
el lector instalado no puede extraer el ZIP oficial que necesitaría para sustituirse.
Los próximos paquetes deben generarse con rutas canónicas; reintentar Update con
el mismo ZIP no corrige el lector ya instalado.

## Verificación y pendientes

`Habitat.Core.Checks -- --update-migration-only --official-package <ZIP>` compara
cliente antiguo y corregido, usando el ZIP oficial y HTTP simulado sin credenciales.
La matriz sintética verifica auxiliares antiguo/actual, éxito/restauración, salida
del auxiliar, timeout y redacción. `tests/release.checks.ps1` comprueba el escritor
real de ZIP bajo Windows PowerShell. `tests/recovery.checks.ps1 -PackagePath <ZIP>`
ejecuta la recuperación oficial en un árbol sintético con `-NoLaunch`: comprueba
hash, cambio de puntero, conservación de la publicación anterior y de fuentes/datos.

Sigue pendiente ejecutar la recuperación en el otro PC y recoger el error original
o hashes de sus DLL. Las pruebas de salud usan un servidor nativo sintético, no una
prueba de todas las integraciones de HABITAT en ese ordenador. No se declara
resuelto allí basándose solamente en las comprobaciones de la versión nueva.
