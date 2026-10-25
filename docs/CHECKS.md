# Comprobaciones de HABITAT

Los comandos se ejecutan desde la raíz del repositorio. Esta página explica cómo verificar; sus instrucciones no son una afirmación de que las pruebas se hayan ejecutado en la revisión actual.

## Documentación

```powershell
node scripts/check-docs.cjs
```

Comprueba los enlaces Markdown locales de la raíz, `docs`, `packaging` y `vscode-extension`, incluidos imágenes, enlaces por referencia y fragmentos de encabezados Markdown. Rechaza rutas absolutas locales para que la documentación funcione en otros clones. Los enlaces HTTP(S) no se consultan. Las rutas dentro de comandos o backticks no se validan: enlaza los archivos que necesiten esa garantía.

## Verificación compartida

Requiere Windows, SDK .NET 10 y Node.js; CI utiliza Node.js 24.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

El [script](../scripts/verify.ps1) ejecuta comprobaciones JavaScript, las suites .NET Appearance, Bridge, Core, Destination, Gmail, Tasks, Weather y Hotkey, pruebas de paquetes, actualizaciones y preparación de firma, y una compilación Release. Se comparte con [pre-push](../.githooks/pre-push) y [GitHub Actions](../.github/workflows/verify.yml).

`-NoRestore` reutiliza dependencias restauradas. `-SkipBuild` omite únicamente la compilación final, no las compilaciones necesarias para ejecutar las suites. Las salidas y los datos de tareas quedan bajo `artifacts/verification`; nunca se utiliza la lista real del usuario. Puente y OAuth sintético pueden necesitar sockets de bucle local; un bloqueo del sandbox debe distinguirse de un fallo del producto.

Para un área concreta, conserva una ruta de salida explícita dentro de `artifacts`. Por ejemplo:

```powershell
$taskCheckOutput = Join-Path (Get-Location) 'artifacts\checks\Tasks\'
dotnet run --project tests/Habitat.Tasks.Checks --configuration Release "-p:BaseOutputPath=$taskCheckOutput"
node tests/extension-translation.checks.cjs
```

La suite de tareas utiliza archivos sintéticos junto a su binario de pruebas. La ruta absoluta calculada desde la raíz mantiene esos archivos dentro de `artifacts`, también al invocar directamente una suite.

## Controles WinUI

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-tasks-ui.ps1
```

El [script WinUI](../scripts/check-tasks-ui.ps1) compila con `HabitatUiChecks=true` y ejecuta comprobaciones nativas con servicios y almacenamiento aislados. Consulta sus parámetros vigentes antes de utilizar selectores específicos. Abre un proceso de pruebas; no instala una release para el usuario. No lo ejecutes para cambios exclusivamente documentales.

Prioriza comprobaciones no visuales. El control visual requiere una necesidad real o una solicitud expresa, conforme a [AGENTS.md](../AGENTS.md).

## Pruebas con aplicaciones y servicios reales

Los modos `--live` y los smoke tests de navegador, Notepad o Codex son optativos y tienen efectos reales: pueden abrir aplicaciones, consultar proveedores o enviar mensajes sintéticos. No forman parte de una revisión de documentación ni de la verificación sintética ordinaria. Antes de ejecutarlos, consulta las condiciones del área en [arquitectura](ARCHITECTURE.md), [Gmail](GMAIL_SETUP.md) o [atajo global](GLOBAL_SHORTCUT.md).

La instalación de paquetes se comprueba por separado con directorios sintéticos y `-NoLaunch -NoShortcuts`; los procedimientos están en [RELEASING.md](RELEASING.md). No mezcles una prueba de instalación con la instalación de uso diario.
