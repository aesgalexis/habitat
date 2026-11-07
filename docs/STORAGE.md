# Retención y limpieza de archivos generados

Implementación local, pendiente de publicación. La política se ejecuta sobre los archivos de cada PC; no contiene rutas de usuario ni requiere los tamaños del diagnóstico de otro equipo.

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
| Archivos recientes | Siete días desde la fecha más reciente de creación o modificación de cualquier elemento del árbol |

Fuera de esas protecciones, se consideran elegibles:

- `artifacts/releases/release-<id>`, el antiguo `artifacts/win-x64` y directorios directamente bajo `artifacts/staged` cuyo manifiesto completo coincida con todos los archivos, tamaños y SHA-256 y contenga los binarios requeridos. Se retiran también compilaciones antiguas sustituidas por una pendiente más reciente. Un stage consumido se registra al terminar correctamente la instalación o el paquete; sigue respetando los siete días.
- Payloads históricos de Inno Setup en `artifacts/packaging/<id>/payload`, únicamente con el mismo manifiesto completo.
- Nuevas carpetas de trabajo de paquetes, verificación y pruebas UI que sus productores hayan registrado expresamente como generadas al terminar. El recibo externo contiene el inventario completo y hashes; cualquier archivo añadido o modificado impide retirarlas.
- Salidas parciales de nuevas compilaciones fallidas, únicamente si `dotnet publish` ha devuelto el control y el productor ha podido registrar un inventario exacto. Un proceso interrumpido sin ese inventario deja su salida conservada.

Los scripts de compilación, instalación, empaquetado y verificación aplican retención al terminar correctamente, aunque HABITAT no se abra. No se borra una compilación al crearla. Los ZIP y EXE finales se conservan como entregables; esta política retira sus carpetas de trabajo duplicadas.

## Límites y seguridad

La limpieza usa exclusivamente `artifacts` de la instalación ZIP activa o de desarrollo. Una instalación EXE puede asociarse explícitamente con los outputs de este repositorio mediante [register-generated-storage.ps1](../scripts/register-generated-storage.ps1), ejecutado desde Windows normal. El registro local queda ligado a esa instalación exacta en `HABITAT/generated-storage.json` del perfil; nunca se incluye en paquetes ni contiene credenciales. Cada PC registra su propia ruta, sin rutas personales en el código. Sin ese registro, un EXE no busca repositorios por el disco. Los ejecutables sueltos y las sesiones de pruebas/sandbox no habilitan la limpieza. La aplicación informa si no puede verificar una ubicación.

Para asociar un EXE que ya incluya esta función con los archivos generados de este clon:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-generated-storage.ps1
```

`-InstallDirectory` admite la ubicación alternativa de la instalación. El registro no instala binarios ni cambia el formato EXE/ZIP. Antes de actualizar una instalación local, compara su versión y commit con la fuente que vas a compilar; conserva las funciones de una instalación más reciente y no sustituyas sus binarios por los de un clon antiguo. Las modificaciones locales deben quedar identificadas en los metadatos y conservar una copia de recuperación.

Nunca se recorren junctions, enlaces, hard links ni puntos de reanálisis de los candidatos. Se comprueban los ancestros y se fijan mediante handles abiertos sin seguimiento de enlaces; los árboles se fijan durante la verificación y el borrado. La eliminación utiliza esos mismos handles, sin `Remove-Item -Recurse` sobre rutas reconstruidas. Solo se admiten las etiquetas Cloud Files documentadas por Microsoft **por encima de `artifacts`**, consultadas desde el handle sin seguimiento de enlaces. Son metadatos sin redirección y permiten trabajar en clones bajo OneDrive. En `artifacts` y todos sus descendientes se rechaza cualquier punto de reanálisis. Un ancestro con otra etiqueta desactiva la limpieza. Referencias: [FILE_ATTRIBUTE_TAG_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_attribute_tag_info), [etiquetas de reanálisis](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-tags).

El bloqueo exclusivo `artifacts/update.lock` coordina limpieza e instalación, incluidos cambio de punteros y recuperación. Los productores registran sus rutas bajo ese bloqueo antes de escribir y mantienen un lease hasta finalizar. Los registros de operaciones interrumpidas sin lease no bastan como prueba de que sus contenidos sean desechables. El rollback restaura tanto `current-release.txt` como `previous-release.txt`.

Antes del primer borrado se guarda un inventario de limpieza. Si se interrumpe, un intento posterior solo retira los archivos restantes que aún coincidan; nunca acepta archivos nuevos. Los errores de un candidato no impiden intentar otros. No se cambian atributos ni se fuerzan archivos bloqueados.

Se conservan código, cambios locales, recuperación, configuraciones, credenciales, tareas, conversaciones y perfiles del navegador. Tampoco se limpian `.nuget`, `.dotnet-local`, `artifacts/tooling`, cachés del SDK, descargas/diagnósticos del perfil ni paquetes finales. El motor no deduplica ni convierte dependencias en enlaces compartidos.

Las carpetas históricas de pruebas, `release-checkouts`, copias de fuente o recuperación y bundles sin recibos no se retiran por su nombre, tamaño o antigüedad. Aunque contengan binarios idénticos, no puede demostrarse que todo el árbol sea prescindible. La primera limpieza puede, por tanto, dejar un volumen considerable sin clasificar. No existe una opción para forzar su borrado desde la app.

## Simulación y comprobaciones

Desde el repositorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/clean-generated-storage.ps1
```

Sin `-Clean` solo analiza. El informe JSON incluye candidatos, bytes, protecciones y fallos. `-Clean` realiza una limpieza real con nueva validación. `HABITAT_STORAGE_DRY_RUN=1` fuerza la simulación; los scripts omiten sus pasadas automáticas repetidas en esa sesión y el análisis explícito sigue disponible. `HABITAT_STORAGE_SKIP_AUTOMATIC=1` omite únicamente el mantenimiento del arranque para validar una instalación, sin deshabilitar las acciones manuales de Storage. Son variables del proceso de validación, no preferencias persistentes; una apertura normal posterior recupera el mantenimiento automático.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/storage.checks.ps1
```

La suite crea instalaciones sintéticas y comprueba activa/anterior, alternativa ante un puntero ausente, stages consumidos/pendientes, leases, bloqueo de actualización, gracia, hashes, cambios locales, errores parciales, idempotencia, recuperación interrumpida y junctions dentro y encima de la raíz. `-TestDirectory` permite una base temporal explícita si las restricciones del sandbox o los ancestros del repositorio impiden las pruebas; nunca usa tareas reales. Las junctions y el instalador pueden requerir ejecutar las comprobaciones desde la sesión normal de Windows. Las comprobaciones WinUI de Preferences verifican etiquetas, botones y resultados con un motor simulado.

Consulta también [CHECKS.md](CHECKS.md), [UPDATES.md](UPDATES.md) y [RELEASING.md](RELEASING.md).
