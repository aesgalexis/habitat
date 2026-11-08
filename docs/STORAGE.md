# Retención y limpieza de archivos generados

La política se ejecuta sobre los archivos de cada PC. Las rutas y los tamaños se determinan localmente.

## Usar la limpieza

Abre **HABITAT menu → Preferences… → Storage → Analyze storage**. El análisis muestra los bytes recuperables y el número de carpetas verificadas. **Clean up** vuelve a analizar con el mismo bloqueo y muestra los bytes realmente liberados; una estimación anterior nunca autoriza un borrado posterior sin validación. Los archivos bloqueados, modificados o no verificables se conservan. Cerrar Preferences cancela el trabajo pendiente.

La limpieza automática se ejecuta en segundo plano treinta segundos después de que se hayan cargado la página principal y el puente local. Reintenta hasta un minuto si hay una actualización activa. Se cancela al cerrar HABITAT o preparar una actualización. El análisis y el borrado no se ejecutan en el hilo de interfaz.

## Política común

El motor [Habitat.GeneratedStorage.cs](../scripts/Habitat.GeneratedStorage.cs) se compila en Habitat.Core y lo carga Windows PowerShell 5.1 mediante [Habitat.Storage.psm1](../scripts/Habitat.Storage.psm1). App y scripts aplican la misma política:

| Se conserva siempre | Condición |
| --- | --- |
| Release activa y directorio del proceso actual | Puntero activo y ejecutable de esta instalación |
| Release anterior | Se respeta el puntero incluso si no se puede verificar; si falta o está dañado, se conserva además la alternativa completa más reciente |
| Compilación pendiente más reciente | Manifiesto completo, aún sin registro de consumo y fuera de una operación activa |
| Directorios de operaciones activas | Registro bajo `.storage-operations` y lease abierto por el productor; se protege también cualquier candidato que los contenga o esté dentro |
| Versiones con procesos HABITAT o HABITAT.Hotkey abiertos | Identidad del ejecutable, además del bloqueo de archivos |
| Salidas históricas o compilaciones fallidas recientes | Siete días desde la fecha más reciente de creación o modificación de cualquier elemento del árbol |

Fuera de esas protecciones, se consideran elegibles:

- `artifacts/releases/release-<id>` y stages generados con identificador único se retiran en la siguiente pasada si ya tienen sustituto y el manifiesto coincide con todos los archivos, tamaños y SHA-256. El antiguo `artifacts/win-x64` y los stages de nombre personalizado conservan el margen de siete días mientras no exista un registro de consumo.
- Payloads históricos de Inno Setup en `artifacts/packaging/<id>/payload`, únicamente con el mismo manifiesto completo.
- Carpetas de trabajo de paquetes y pruebas con recibo de finalización, y stages consumidos por una instalación o un empaquetado. Son elegibles en cuanto terminan sus operaciones. El recibo externo contiene el inventario completo y hashes. Cualquier archivo añadido o modificado impide retirarlas.
- Salidas parciales de nuevas compilaciones fallidas, únicamente si `dotnet publish` ha devuelto el control y el productor ha podido registrar un inventario exacto. Un proceso interrumpido sin ese inventario deja su salida conservada.

Los scripts de compilación, instalación, empaquetado y verificación aplican retención al terminar correctamente, aunque HABITAT no se abra. La compilación pendiente se puede activar hasta que se consuma o una nueva la sustituya. Un stage suministrado al empaquetador sigue la política de compilaciones pendientes. La publicación compartida retira el suyo después de validar ZIP y EXE. El motor conserva los ZIP y EXE finales como entregables; quien realiza la publicación debe retirar esas copias locales cuando haya verificado su entrega.

## Cierre del trabajo local

Al terminar una publicación, elimina sus worktrees de preparación una vez comprobados sus cambios locales, archivos ignorados y referencias en Git. Preserva aparte las configuraciones, los diagnósticos necesarios y cualquier cambio único. Desvincula las junctions de NuGet sin recorrer su destino antes de retirar una copia de trabajo.

Conserva una sola versión del SDK local tras verificar que permite compilar y ejecutar las comprobaciones. Las cachés de paquetes compartidas se reutilizan para evitar descargas y compilaciones repetidas. Las copias de código para pruebas deben cerrarse junto con la prueba, guardando únicamente los cambios únicos y su procedencia cuando hagan falta.

Los respaldos importantes son los datos, las configuraciones y los cambios de fuente sin publicar. Los ejecutables de versiones antiguas y las copias completas de dependencias no sustituyen esos respaldos. La limpieza histórica requiere clasificar los archivos y verificar las copias preservadas antes de borrar. El motor automático no extiende su alcance a fuentes, SDK, datos personales o paquetes finales.

El actualizador tiene su propio directorio de trabajo por intento bajo `%LOCALAPPDATA%\HABITAT\updates`. Lo retira al cancelar antes de la entrega al instalador o al confirmar el arranque de la versión nueva. Los diagnósticos se escriben aparte y se conservan junto a `last-result.json`. Un error de instalación posterior a la entrega conserva la carpeta de trabajo para investigar el fallo.

## Límites y seguridad

El análisis de Storage usa `artifacts` de la instalación ZIP activa o de desarrollo. Una instalación EXE puede asociarse explícitamente con los outputs de este repositorio mediante [register-generated-storage.ps1](../scripts/register-generated-storage.ps1), ejecutado desde Windows normal. El registro local queda ligado a esa instalación exacta en `HABITAT/generated-storage.json` del perfil; nunca se incluye en paquetes ni contiene credenciales. Cada PC registra su propia ruta, sin rutas personales en el código. Sin ese registro, un EXE no busca repositorios por el disco. Los ejecutables sueltos y las sesiones de pruebas/sandbox no habilitan la limpieza. La aplicación informa si no puede verificar una ubicación.

Para asociar un EXE que ya incluya esta función con los archivos generados de este clon:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-generated-storage.ps1
```

`-InstallDirectory` admite la ubicación alternativa de la instalación. El registro no instala binarios ni cambia el formato EXE/ZIP. Antes de actualizar una instalación local, compara su versión y commit con la fuente que vas a compilar; conserva las funciones de una instalación más reciente y no sustituyas sus binarios por los de un clon antiguo. Las modificaciones locales deben quedar identificadas en los metadatos y conservar una copia de recuperación.

Nunca se recorren junctions, enlaces, hard links ni puntos de reanálisis de los candidatos. Se comprueban los ancestros y se fijan mediante handles abiertos sin seguimiento de enlaces; los árboles se fijan durante la verificación y el borrado. La eliminación utiliza esos mismos handles, sin `Remove-Item -Recurse` sobre rutas reconstruidas. Solo se admiten las etiquetas Cloud Files documentadas por Microsoft **por encima de `artifacts`**, consultadas desde el handle sin seguimiento de enlaces. Son metadatos sin redirección y permiten trabajar en clones bajo OneDrive. En `artifacts` y todos sus descendientes se rechaza cualquier punto de reanálisis. Un ancestro con otra etiqueta desactiva la limpieza. Referencias: [FILE_ATTRIBUTE_TAG_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_attribute_tag_info), [etiquetas de reanálisis](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-tags).

El bloqueo exclusivo `artifacts/update.lock` coordina limpieza e instalación, incluidos cambio de punteros y recuperación. Los productores registran sus rutas bajo ese bloqueo antes de escribir y mantienen un lease hasta finalizar. Los registros de operaciones interrumpidas sin lease no bastan como prueba de que sus contenidos sean desechables. El rollback restaura tanto `current-release.txt` como `previous-release.txt`.

Antes del primer borrado se guarda un inventario de limpieza. Si se interrumpe, un intento posterior solo retira los archivos restantes que aún coincidan; nunca acepta archivos nuevos. Los errores de un candidato no impiden intentar otros. No se cambian atributos ni se fuerzan archivos bloqueados.

Se conservan código, cambios locales, recuperación, configuraciones, credenciales, tareas, conversaciones y perfiles del navegador. También se conservan `.nuget`, `.dotnet-local`, `artifacts/tooling`, cachés del SDK, diagnósticos y paquetes finales.

Las carpetas históricas de pruebas, `release-checkouts`, copias de fuente o recuperación y bundles sin recibos no se retiran por su nombre, tamaño o antigüedad. Aunque contengan binarios idénticos, no puede demostrarse que todo el árbol sea prescindible. La primera limpieza puede, por tanto, dejar un volumen considerable sin clasificar. No existe una opción para forzar su borrado desde la app.

## Simulación y comprobaciones

Desde el repositorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/clean-generated-storage.ps1
```

Sin `-Clean` solo analiza. El informe JSON incluye candidatos, bytes, protecciones y fallos. `-Clean` realiza una limpieza real con nueva validación. `HABITAT_STORAGE_DRY_RUN=1` fuerza la simulación. `HABITAT_STORAGE_SKIP_AUTOMATIC=1` omite las pasadas automáticas en el arranque y en los scripts, mientras el análisis y la limpieza manual siguen disponibles. Ambas variables se aplican al proceso que ejecuta la validación.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/storage.checks.ps1
```

La suite crea instalaciones sintéticas y comprueba activa/anterior, alternativa ante un puntero ausente, stages consumidos/pendientes, leases, bloqueo de actualización, gracia, hashes, cambios locales, errores parciales, idempotencia, recuperación interrumpida y junctions dentro y encima de la raíz. `-TestDirectory` permite una base temporal explícita si las restricciones del sandbox o los ancestros del repositorio impiden las pruebas; nunca usa tareas reales. Las junctions y el instalador pueden requerir ejecutar las comprobaciones desde la sesión normal de Windows. Las comprobaciones WinUI de Preferences verifican etiquetas, botones y resultados con un motor simulado.

Consulta también [CHECKS.md](CHECKS.md), [UPDATES.md](UPDATES.md) y [RELEASING.md](RELEASING.md).
