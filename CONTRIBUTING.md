# Colaborar en HABITAT

Esta guía describe el flujo de desarrollo. Las reglas del repositorio están en [AGENTS.md](AGENTS.md). Para instalar una publicación sin herramientas de desarrollo, consulta [la puesta en marcha](docs/GETTING_STARTED.md).

## Entorno y primera compilación

Trabaja desde la raíz del clon en Windows x64. Necesitas el SDK .NET 10 para compilar y Node.js para las comprobaciones JavaScript; CI utiliza Node.js 24. NuGet restaura las dependencias .NET. Inno Setup solo es necesario para generar el EXE de distribución; consulta [RELEASING.md](docs/RELEASING.md).

```powershell
dotnet restore Habitat.sln -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-winui.ps1
```

Sin `-Install`, el script prepara una compilación nueva bajo `artifacts/staged`, valida su manifiesto y muestra cómo activarla. Para compilar e instalar en la copia de desarrollo, ejecuta desde tu sesión normal de Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-winui.ps1 -Install
```

El instalador espera el trabajo en curso y conserva una publicación anterior. `HABITAT.vbs` resuelve la publicación activa. Compilación y pruebas pueden ejecutarse dentro del sandbox; la aplicación que usa el usuario debe iniciarse desde su sesión normal, como exige `AGENTS.md`.

## Verificar un cambio

```powershell
node scripts/check-docs.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

El primer comando solo comprueba documentación. El segundo ejecuta la verificación compartida y compila la solución. Consulta [CHECKS.md](docs/CHECKS.md) para seleccionar comprobaciones adicionales y conocer sus efectos.

Mantén los datos de prueba bajo `artifacts`, especialmente las tareas. No utilices cuentas, conversaciones ni credenciales reales en pruebas ordinarias. Comprueba la interfaz en inglés, accesibilidad, conservación de borradores y estructura inicial común cuando cambies un destino.

## Documentación junto al cambio

- Actualiza la guía asociada cuando cambien comportamiento, etiquetas o comandos; enlázala desde el [índice](docs/README.md).
- Distingue **implementado en esta copia**, **declarado en metadatos**, **publicado** y **pendiente de reconciliar**. Una compilación local o una entrada del changelog no demuestra publicación.
- En documentos de estado, anota la fecha, la base de Git y si existen cambios locales. No presentes una ejecución antigua de pruebas como validación del cambio actual.
- Usa enlaces relativos a archivos reales. Para evidencia histórica del código, utiliza un permalink al commit exacto si conserva el estado auditado; de lo contrario, identifica la ruta y revisión como referencias históricas.
- Conserva una referencia principal por tema: [actualizaciones](docs/UPDATES.md), [distribución](docs/RELEASING.md), [idioma](docs/LOCALIZATION.md) y [firma](docs/SIGNING.md). Los resúmenes deben enlazarla.

El comprobador valida enlaces locales, fragmentos de encabezados Markdown y archivos citados mediante enlaces. No interpreta rutas dentro de comandos o backticks ni consulta servicios externos. Convierte las referencias de archivos que deban verificarse en enlaces Markdown.

## Trabajo simultáneo y uso de IA

Antes de editar, revisa `git status --short` y el diff de los archivos afectados. Si otra persona o IA trabaja en el mismo clon, delimita archivos, relee cada archivo antes de aplicar cambios y conserva las ediciones ajenas. Evita formateos globales, `reset`, `clean` y restauraciones que borren trabajo compartido. Si aparece una edición incompatible en el mismo fragmento, resuelve la coordinación antes de sobrescribirlo.

No crees subagentes sin autorización explícita del usuario. El uso de IA no cambia las reglas de pruebas, datos, idioma o publicación. No hagas commits, pushes ni publicaciones como efecto implícito de una tarea de documentación.

## Hooks y checkpoints

La activación de hooks es una decisión explícita del colaborador:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```

Este comando configura `core.hooksPath` a `.githooks`. El hook [pre-commit](.githooks/pre-commit) busca algunos patrones de secretos; no sustituye una revisión. [Pre-push](.githooks/pre-push) ejecuta `scripts/verify.ps1` para los pushes. [Post-commit](.githooks/post-commit) hace push automático únicamente desde `codex/autosave`: un commit en esa rama puede publicar código en GitHub inmediatamente.

Para consultar la configuración local, usa `git config --get core.hooksPath`. No actives ni cambies los hooks de otra persona durante una tarea sin que forme parte del encargo. Las publicaciones siguen el procedimiento de [RELEASING.md](docs/RELEASING.md); los checkpoints no son releases.
