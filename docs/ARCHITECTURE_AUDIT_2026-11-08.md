# Auditoría de HABITAT — 8 de noviembre de 2026

## Diagnóstico

La estructura sigue siendo coherente para una aplicación de escritorio con integraciones. Hay composición explícita, sesiones comprobables, adaptadores separados y controles sólidos para proteger tareas, contactos y entregas inciertas. La deuda se concentra en la coordinación de MainPage, la recuperación desigual de configuraciones y los límites del proceso de verificación y almacenamiento.

Conviene consolidar esos puntos antes de ampliar las integraciones. Las mejoras propuestas mantienen las sesiones existentes y se pueden entregar por separado.

## Alcance y evidencia

- Árbol local de versión **0.20.0**, con base Git `12fd203ec55425d30121695c13f873c78e6575bd`.
- Al iniciar la revisión había **32 entradas locales pendientes**, incluidos dos archivos sin seguimiento. Los hallazgos incluyen esos cambios y no certifican un paquete publicado.
- Inspección de composición, coordinación de interfaz, persistencia, sincronización Drive, actualizaciones, extensiones, pruebas y workflows. La instalación personal y los datos reales del usuario quedan fuera de las pruebas.
- Cuatro proyectos de producción. MainPage de producción reúne **5.127 líneas en 28 archivos C# parciales**. Son líneas físicas, incluidos comentarios y espacios; sirven para localizar responsabilidades, no para medir calidad por sí solas.
- Se compiló una prueba aislada contra el Core actual, con cero errores y advertencias. Reprodujo dos escenarios de sustitución de preferencias tras un fallo de lectura y verificó un escenario válido de control.
- Pasaron 61 comprobaciones del worker de Chrome, 15 de consentimiento, la suite sintética de VS Code y la comprobación de localización, que informa 650 mensajes y 785 referencias. La prueba VS Code requirió ejecutar fuera del sandbox por el bloqueo de su socket local; usa transporte y terminales simulados.
- La evidencia y el pequeño reproductor quedan en `artifacts/architecture-audit-2026-11-08`. Sus binarios y datos sintéticos se retiran al terminar. Esta revisión no ejecuta la suite completa, WinUI, una publicación ni pruebas con servicios externos. Tampoco mide el consumo del perfil real ni el tiempo total actual de CI.

## Mejoras por prioridad

### 1. Alta — Proteger las preferencias ante fallos de lectura

**Reproducido.** [Preferences.Load](../src/Habitat.Core/Services/Storage/Preferences.cs) devuelve valores por defecto cuando el JSON es inválido o la lectura falla con IOException/UnauthorizedAccessException. El objeto devuelto admite Save, que sustituye el archivo original mediante AtomicFile. No distingue un perfil nuevo de una configuración existente que no se pudo leer.

Con un bloqueo temporal de un archivo válido, Load devolvió valores por defecto. Tras liberar el bloqueo, Save sustituyó la configuración original y no creó un respaldo. Ocurrió lo mismo con JSON dañado. El control con un archivo válido conservó sus valores; su reserialización normaliza el documento.

Existe una ruta productiva hacia ese guardado: [MainPage](../src/Habitat.App/MainPage.xaml.cs) guarda preferencias al detenerse y al guardar la posición de la ventana. El riesgo afecta a preferencias, destinos configurados y otros ajustes guardados en ese documento. Las tareas y los contactos tienen almacenes distintos.

**Propuesta.** Devolver un estado de carga explícito —nuevo, válido, dañado o temporalmente inaccesible— y bloquear la sustitución automática tras un error. Reintentar los fallos transitorios y conservar el original antes de una recuperación explícita. Reutilizar AtomicFile para la escritura final.

**Aceptación.** Un bloqueo breve o JSON inválido no altera los bytes originales al mover/cerrar la ventana. El usuario puede recuperar la configuración y el caso de perfil nuevo sigue funcionando. Añadir regresiones de estos tres escenarios a la suite existente.

### 2. Alta — Poner límites verificables a las comprobaciones y al empaquetado

**Observado en código.** [verify.ps1](../scripts/verify.ps1) espera las suites .NET con WaitForExit sin timeout. [build-release-assets.ps1](../scripts/build-release-assets.ps1) espera los trabajos ZIP y EXE con Wait-Job sin timeout. Sus bloques finally no pueden resolver una espera que nunca termina. Las pruebas WinUI sí tienen un límite explícito.

[verify.yml](../.github/workflows/verify.yml) tampoco define un timeout de job ni cancela verificaciones sustituidas por nuevos commits. Cada PR activa ambos jobs completos, incluidos cambios solo documentales. No he reproducido un bloqueo ni atribuyo los 44 minutos anteriores a una causa concreta sin sus tiempos por fase.

**Propuesta.** Añadir un límite por suite, un presupuesto global y cancelación de sus procesos descendientes, conservando salida parcial y el nombre de la fase bloqueada. Cancelar ejecuciones de verificación sustituidas en la misma PR. Introducir perfiles explícitos para documentación, área afectada y release completa; los cambios compartidos o no clasificados deben seleccionar la verificación completa. Reutilizar el resultado del mismo commit y conservar los contratos de instalación y recuperación.

**Aceptación.** Una suite sintética bloqueada produce un fallo acotado y un diagnóstico útil. Un cambio solo documental no compila WinUI. Publicar informa tiempos de compilación, pruebas, empaquetado y subida. El objetivo de una ruta crítica habitual de unos 5–6 minutos debe verificarse con mediciones, distinguiendo espera de runners y ejecución; todavía no es una garantía.

### 3. Alta — Consolidar los cambios pendientes en una versión reproducible

**Observado en Git.** Las optimizaciones del flujo y de la retención forman parte de las 32 entradas pendientes. [build-release-assets.ps1](../scripts/build-release-assets.ps1) y [Habitat.Checks.slnf](../tests/Habitat.Checks.slnf) están sin seguimiento y ya son dependencias del flujo local. Un checkout del commit base todavía no reproduce ese árbol.

**Propuesta.** Revisar y cerrar los cambios existentes en commits acotados de verificación/publicación y retención. Incluir los dos archivos nuevos. Verificar el commit exacto y utilizar esa identidad para el paquete y el resultado de CI.

**Aceptación.** Un checkout limpio del commit acordado contiene todas las dependencias del pipeline. La versión instalada, el commit de origen y las comprobaciones correspondientes se pueden relacionar sin depender de archivos locales. Esta auditoría solo propone ese cierre; no realiza commits ni despliegues.

### 4. Media — Reducir la coordinación compartida en MainPage

**Observado en código.** Los 28 parciales comparten estado privado. [TickPage](../src/Habitat.App/MainPage.xaml.cs) se invoca cada 250 ms desde el dispatcher y coordina actualizaciones, Gmail, Drive, tareas, mercados, programación, tiempo, apariencia y disponibilidad de envío. Cada servicio ya limita parte de su actividad; este dato no demuestra que haga peticiones de red con esa frecuencia. Falta medir cuánto trabajo y renderizado provoca en reposo.

Ya se han extraído correctamente [ContactDestinationSession](../src/Habitat.Core/Features/Contacts/ContactDestinationSession.cs), [ContactDestinationViews](../src/Habitat.App/Features/Destinations/ContactDestinationViews.cs) y [CodexScheduleController](../src/Habitat.Core/Features/Codex/CodexScheduleController.cs). El siguiente esfuerzo debe aprovechar esas fronteras.

**Propuesta.** Extraer progresivamente la presentación y el ciclo de vida de Gmail y Tasks, que siguen concentrados en [MainPage.Gmail](../src/Habitat.App/MainPage.Gmail.cs) y [MainPage.Tasks](../src/Habitat.App/MainPage.Tasks.cs). Hacer que los módulos notifiquen cambios y posean sus plazos. Mantener en la página la navegación, el editor compartido y la composición visual.

**Aceptación.** Cambiar el sondeo de Gmail o de tareas no exige modificar el tick común. Las pruebas de cada módulo comprueban cancelación, cierre y estado sin construir toda la página. Una medición antes/después muestra actividad en reposo y renderizados, conservando foco, borradores y la pantalla inicial común.

### 5. Media — Compactar respaldos conservando la recuperación de datos

**Observado en código; crecimiento inferido.** [TaskStore](../src/Habitat.Core/Services/Storage/TaskStore.cs) archiva listas completas y [ContactStore](../src/Habitat.Core/Features/Contacts/ContactStore.cs) conserva documentos completos por revisión. [TaskHistoryIndex.Catalog](../src/Habitat.Core/Services/Storage/TaskHistoryIndex.cs) enumera y ordena los archivos del historial para validar su índice. El coste crece con las revisiones aunque el índice evite volver a leer todos los documentos.

[DriveDocumentClient](../src/Habitat.Core/Features/Drive/DriveDocumentClient.cs) también conserva revisiones inmutables completas en el espacio privado de Drive. Estas revisiones soportan recuperación y concurrencia entre PCs; eliminarlas por antigüedad rompería esas garantías. La limpieza de artifacts no resuelve este crecimiento. No se ha medido cuánto ocupa actualmente el historial personal.

**Propuesta.** Diseñar primero una compactación local con una instantánea verificada y cambios posteriores, preservando todos los datos e historial recuperables comprometidos por el producto. Probar restauración e interrupciones antes de retirar revisiones redundantes. Tratar la compactación remota como un cambio de protocolo separado, compatible con equipos desconectados y versiones anteriores.

**Aceptación.** Un ensayo con miles de cambios mantiene un presupuesto medido de espacio y tiempo de carga. La restauración conserva tareas, contactos y revisiones requeridas, incluso tras una interrupción de la compactación. La sincronización sigue reconociendo equipos que llevan tiempo desconectados.

### 6. Media — Unificar diagnósticos con límites de espacio

**Observado en código.** [AppServices.ReportError](../src/Habitat.App/Services/AppServices.cs) añade registros a operations.log sin rotación. [UpdateDiagnosticLog](../src/Habitat.Core/Services/Updates/UpdateDiagnosticLog.cs) limita y redacta el contenido de cada mensaje, pero no establece aquí un presupuesto total de sus archivos. TaskStore también añade un registro por carga. [FacebookMessageTrace](../src/Habitat.Core/Features/Facebook/FacebookMessageTrace.cs) ya ofrece un precedente de rotación al superar 512 KiB.

**Propuesta.** Compartir una política de tamaño, número de archivos y deduplicación de errores repetidos. Mantener identificador de operación, fase, versión y error redactado. La retención de diagnósticos debe estar separada del historial de datos.

**Aceptación.** Una simulación de errores repetidos conserva el fallo reciente y su contexto dentro del presupuesto acordado. Los registros no incluyen contenido de mensajes ni credenciales. Un fallo al registrar nunca sustituye el resultado de la operación original.

### 7. Media — Fijar el entorno de compilación y reutilizar su caché

**Observado en archivos versionados y nuevos del árbol.** CI solicita `10.0.x`; el SDK local conservado es 10.0.403. No hay global.json ni archivos de bloqueo NuGet en las fuentes del proyecto. Las referencias directas de App sí fijan sus versiones. Los workflows revisados no configuran una caché de paquetes reutilizable.

**Propuesta.** Declarar un SDK común en el repositorio, actualizarlo deliberadamente y hacer que local y CI lean esa versión. Fijar la resolución de paquetes y usar una restauración que compruebe ese bloqueo. Cachear la ruta real de NuGet utilizada por los scripts, con una clave que incluya plataforma, SDK y resolución de dependencias.

**Aceptación.** Dos checkouts del mismo commit utilizan el mismo SDK y la misma resolución de paquetes. Una actualización del entorno aparece como un cambio revisable. La caché mejora el tiempo sin permitir que se omitan las comprobaciones del commit.

### 8. Media — Convertir la retención acordada en un cierre habitual del trabajo

**Observado en el flujo.** Los scripts ya registran operaciones y salidas terminadas para la limpieza común. La retirada de paquetes finales y worktrees depende todavía del cierre manual descrito en [AGENTS.md](../AGENTS.md) y [STORAGE](STORAGE.md). La limpieza histórica necesitó inventariar excepciones y conservar fuentes/configuraciones únicas.

**Propuesta.** Añadir un resumen de cierre por trabajo que identifique las salidas creadas, su entrega verificada y qué temporales se retiraron o conservaron con motivo. Reutilizar los recibos y leases existentes. El cierre debe preservar cambios y configuraciones únicos antes de retirar una copia de trabajo, y desvincular la caché compartida sin recorrerla.

**Aceptación.** Varias ejecuciones correctas consecutivas dejan estable el volumen de temporales, aparte de la compilación en uso y la pendiente que corresponda. Los paquetes terminados tienen una entrega verificable antes de retirar su copia local. Los trabajos fallidos conservan diagnóstico acotado y una clasificación explícita de su salida.

## Orden de ejecución recomendado

1. Corregir la recuperación de preferencias, añadir límites de espera y consolidar los cambios ya pendientes.
2. Fijar SDK/dependencias, perfilar las comprobaciones y cerrar automáticamente los temporales conocidos.
3. Separar Gmail y Tasks de la coordinación global, con medidas de actividad en reposo.
4. Diseñar compactación y recuperación de datos como un trabajo específico, con pruebas de interrupción y compatibilidad entre equipos.

Los riesgos de pérdida de configuración y esperas ilimitadas tienen un alcance más concreto que una refactorización global y permiten obtener resultados verificables antes.

## Aspectos que conviene conservar

La composición manual de [AppServices](../src/Habitat.App/Services/AppServices.cs), las sesiones de dominio, [ComposerCoordinator](../src/Habitat.Core/Features/Composer/ComposerCoordinator.cs), las protecciones ante entregas inciertas, las escrituras duraderas, las revisiones optimistas y el aislamiento de pruebas son bases útiles. Mantener una única construcción compartida para ZIP/EXE y la limpieza común de salidas verificadas evita divergencias y duplicados.

Los hallazgos antiguos sobre la inclusión de bin/obj, la ruta duplicada del actualizador, la configuración desigual de Drive en ZIP/EXE y la ausencia de WinUI en el flujo común ya tienen correcciones en el árbol revisado. La firma sigue aplazada conforme a [SIGNING](SIGNING.md).
