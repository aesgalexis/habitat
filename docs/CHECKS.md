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

La verificación local ejecuta JavaScript, las diez suites .NET, las pruebas de paquetes y almacenamiento y la suite nativa WinUI. Una sola compilación Release produce las suites .NET, que se ejecutan en paralelo con datos aislados. GitHub ejecuta la parte nativa en otro runner en paralelo con las comprobaciones de lógica. El hook pre-push comprueba el whitespace de los commits que se envían. [Verificación](../scripts/verify.ps1) · [Hook pre-push](../.githooks/pre-push) · [GitHub Actions](../.github/workflows/verify.yml).

`-NoRestore` reutiliza la restauración previa de .NET. La fixture mínima de compilación restaura sus referencias del SDK. `-SkipBuild` usa un filtro de la solución para compilar solo las suites .NET y su verificador. `-SkipUi` ejecuta las comprobaciones de consola. La publicación exige que pasen tanto la lógica como WinUI para el commit etiquetado. Las salidas y los datos de tareas de ensayo se generan en `artifacts/verification` y se retiran tras una ejecución correcta. Las pruebas del puente y de OAuth sintético usan sockets de bucle local.

Para un área concreta, conserva una ruta de salida explícita dentro de `artifacts`. Por ejemplo:

```powershell
$taskCheckOutput = Join-Path (Get-Location) 'artifacts\checks\Tasks\'
dotnet run --project tests/Habitat.Tasks.Checks --configuration Release "-p:BaseOutputPath=$taskCheckOutput"
node tests/extension-translation.checks.cjs
```

La suite de tareas utiliza archivos sintéticos junto a su binario de pruebas. La ruta absoluta calculada desde la raíz mantiene esos archivos dentro de `artifacts`, también al invocar directamente una suite.

## Controles WinUI

La [suite de almacenamiento](../tests/storage.checks.ps1) verifica la limpieza destructiva exclusivamente sobre instalaciones sintéticas y acepta `-TestDirectory` para una base temporal aislada. El [procedimiento de simulación](STORAGE.md) permite analizar una instalación real sin borrar sus archivos. Durante el desarrollo de la limpieza se usa `HABITAT_STORAGE_DRY_RUN=1` al compilar/instalar y al iniciar la aplicación real.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-tasks-ui.ps1
```

La verificación común da hasta 600 segundos a cada proceso de WinUI. Compila con `HabitatUiChecks=true` y utiliza servicios y almacenamiento aislados para probar también Gmail, VS Code y el drenaje de secciones. `-ContactDestinationsOnly` cubre la agenda manual, las fuentes antiguas, el rechazo de duplicados y la selección de un contacto. Su alias es `-ContactSourcesOnly`. `-ContactChannelsOnly` cubre la selección de canal, dirección y remitente, la ficha mínima y los flujos interceptados de Gmail, Messenger y Phone Link. [Parámetros del script](../scripts/check-tasks-ui.ps1).

La ejecución completa reutiliza el binario de prueba en un segundo proceso para comprobar el menú y el ciclo de activación de `MainWindow`. CI conserva `tasks-ui-checks.log` y `window-menu-checks.log` cuando falla la verificación.

Prioriza comprobaciones no visuales. El control visual requiere una necesidad real o una solicitud expresa, conforme a [AGENTS.md](../AGENTS.md).

Desde 0.18.2, los casos de Contacts abren Preferences con Drive desconectado y con un vínculo guardado, prueban ambos temas y verifican el desplegable avanzado tras refrescar. Los casos de programación de Codex añaden dos ventanas nativas sintéticas: verifican selección por chat, transferencia y confirmación en la ventana fijada, invocación del botón Send desde segundo plano y conservación del otro borrador. El proveedor puede activar la ventana verificada; no se inyecta Enter en ese caso. Los servicios y credenciales están aislados y no se abre ni se envía nada al Codex real.

`-PreferencesOnly` comprueba las categorías de Preferences, la conservación de cambios y las acciones de Storage con resultados simulados. `-TimeoutSeconds` ajusta el máximo de cada proceso. Su valor por defecto es 180 segundos. Una ejecución directa usa `artifacts/ui-checks/run-<id>` y retira esa carpeta cuando las comprobaciones pasan. Puedes indicar `-OutputDirectory` para conservar los archivos de una ejecución.

## Pruebas con aplicaciones y servicios reales

Desde 0.20.0, Contacts incluye pruebas del protocolo compartido de Drive y de Tasks con dos equipos simulados: concurrencia, revisión por tarea y decisiones caducadas, edición durante la red, respuesta perdida, pausa/reanudación, errores independientes, migración del mismo CSV, identidad y recuperación persistente. `-ContactDestinationsOnly` también comprueba **Preferences → Google Drive**, el acceso desde Contacts, **Sync contacts** y **Sync tasks** independientes y persistentes y el desplegable avanzado en ambos temas. Estas pruebas no usan Google real ni la lista de tareas del usuario. Consulta [TASKS_DRIVE_SYNC.md](TASKS_DRIVE_SYNC.md).

Los modos `--live` y los smoke tests de navegador, Notepad o Codex son optativos y tienen efectos reales: pueden abrir aplicaciones, consultar proveedores o enviar mensajes sintéticos. No forman parte de una revisión de documentación ni de la verificación sintética ordinaria. Antes de ejecutarlos, consulta las condiciones del área en [arquitectura](ARCHITECTURE.md), [Gmail](GMAIL_SETUP.md) o [atajo global](GLOBAL_SHORTCUT.md).

La instalación de paquetes se comprueba por separado con directorios sintéticos y `-NoLaunch -NoShortcuts`; los procedimientos están en [RELEASING.md](RELEASING.md). No mezcles una prueba de instalación con la instalación de uso diario.

## Compilación y capacidades de paquetes

[build.checks.ps1](../tests/build.checks.ps1) conserva fuentes deliberadamente inválidas bajo salidas antiguas `bin` y `obj` y comprueba que tanto una compilación normal como una con `BaseOutputPath` distinto las ignoran. No necesita borrar salidas del desarrollador.

El verificador lee metadatos PE. `-RequireDrive` exige configuración integrada y `-ReferenceDirectory` compara los seis binarios propios con un directorio de referencia. La suite Core comprueba el recurso con configuración sintética y un perfil nuevo sin red. La prueba del payload comprueba también la extracción de un ZIP y el rechazo de binarios alterados. [Verificador](../scripts/check-release-payload.ps1) · [Prueba del payload](../tests/payload.checks.ps1).
