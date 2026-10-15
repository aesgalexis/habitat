# Arquitectura interna de HABITAT

Implementación posterior a la auditoría del 15 de octubre de 2026. La interfaz y sus textos, accesibilidad, destinos, atajos, borradores y plazos se mantienen. No hay nuevos frameworks ni paquetes de aplicación.

## Responsabilidades

| Área | Propietario |
| --- | --- |
| Creación de servicios, producción frente a pruebas y lanzadores | `Habitat.App/Services/AppServices` |
| Marco, activación y cierre de la ventana | `MainWindow`, a través de `IWindowHost` |
| Controles, estilos, distribución y foco | `MainPage` y sus archivos parciales |
| Estado de conversación y entrega | `Habitat.Core/Features/Conversation` |
| Lista, paginación, selección y autorización de correo | `Habitat.Core/Features/Gmail/GmailSession` |
| Serialización de cambios de tareas y conflictos | `Habitat.Core/Features/Tasks/TaskSession` |
| Obtención de tiempo, descarte de solicitudes obsoletas y reintentos | `Habitat.Core/Features/Weather/WeatherSession` |
| HTTP, OAuth, JSON, credenciales, almacenamiento y reglas puras | `Habitat.Core/Services` y `Models` |
| Integración WinRT con ubicación, portapapeles y apps | `Habitat.App/Services` |

Las pruebas y la herramienta de Gmail referencian la misma biblioteca que la aplicación. Ya no se recompilan copias enlazadas de archivos de producción. La solución incluye todos los proyectos. Los modelos internos se comparten con los ensamblados autorizados mediante `InternalsVisibleTo`.

La página recibe sus servicios y una interfaz de ventana explícitos; no busca `Application.Current.MainWindow`. El estado de conversación tampoco se infiere de la presencia de controles. Los parciales conservan la construcción y el estilo existentes para reducir el riesgo de regresión visual.

## Aislamiento y cierre

El modo `HABITAT_UI_CHECKS` exige `AppDataPaths.Isolated`. Inyecta tareas y preferencias bajo artefactos, credenciales de Gmail en memoria, un manejador HTTP que rechaza cualquier solicitud, ubicación sintética y lanzadores que no abren programas. También bloquea el arranque del puente desde ese contexto. Los diagnósticos permanecen en los directorios de prueba. La compilación de prueba no cambia la versión instalada.

`AsyncOperationScope` registra operaciones, impide nuevas entradas durante el cierre, cancela y espera las tareas pendientes. La ventana cancela su primer cierre, guarda la posición y espera a la página y los servicios antes de cerrarse definitivamente. Los servicios se liberan en bloques `finally`, también ante fallos de cancelación o parada. El puente serializa arranque y cierre, elimina el servidor tras un arranque fallido y permite repetir `DisposeAsync`.

## Persistencia y rendimiento

`AppDataPaths` centraliza rutas y resolución del perfil de escritorio. Se conservan las ubicaciones previas: preferencias y diagnósticos locales en el perfil de ejecución, tareas en el perfil persistente de escritorio y credenciales en Windows. Las comprobaciones de perfil temporal y redirección de tareas siguen activas. Los tests siempre pasan rutas explícitas.

Las preferencias se escriben en un archivo temporal con flush y reemplazo atómico. Un error conserva el archivo anterior, devuelve un resultado y deja un diagnóstico interno sin modificar las etiquetas de la interfaz.

`TaskSession` serializa lecturas y modificaciones con un semáforo y ejecuta la E/S en el pool de hilos. Cada modificación se calcula a partir del último estado confirmado; solo se limpia el borrador si sigue siendo el que se guardó. Los conflictos externos bloquean cambios hasta recargar, igual que antes.

`tasks.json.history/index.json` es un resumen descartable con checksum de las revisiones verificadas. El catálogo incluye nombres, tamaños y fechas de modificación. Si cambia el catálogo o el índice no es válido, se recorren y validan los originales. El índice no sustituye las copias ni elimina revisiones. La detección de IDs ausentes usa un conjunto en lugar de búsquedas anidadas.

En el ensayo sintético de 1.000 tareas y 200 revisiones, la verificación final registró **646,7 ms de lectura completa y 28,8 ms con índice**. Es una medida local, no una garantía para otros discos o historiales. Se mantienen todas las revisiones para conservar la recuperación; no se ha introducido una política de borrado ni una migración de datos.

## Compilación y actualización

1. `build-winui.ps1` publica en un directorio nuevo bajo `artifacts/staged`.
2. Se genera y valida `build-manifest.json` con tamaño y SHA-256 de cada archivo.
3. El instalador copia y vuelve a verificar una versión independiente en `artifacts/releases/release-<id>`.
4. Consulta el estado autenticado del puente y su proceso, espera a que termine la solicitud y cierra la instancia de este proyecto.
5. Cambia `current-release.txt` mediante reemplazo atómico. El lanzador estable `HABITAT.vbs` resuelve ese archivo; no se sobrescribe el directorio de una versión activa.
6. Arranca y comprueba que el nuevo proceso ofrece el contrato de diagnóstico esperado. Si falla, restaura el selector anterior y vuelve a abrir la versión previa. Las versiones permanecen disponibles para diagnóstico y recuperación.

La instalación antigua en `artifacts/win-x64` sigue siendo una ruta de compatibilidad. `previous-release.txt` permite al lanzador recuperar la anterior si el selector actual no apunta a un ejecutable disponible. Las actualizaciones concurrentes se excluyen con un bloqueo de archivo; los directorios externos y enlaces del sistema de archivos se rechazan. No hay borrados recursivos de versiones.

La aplicación del usuario se inicia exclusivamente desde la sesión normal de Windows. El instalador rechaza identidades empaquetadas o perfiles temporales. Las pruebas de distribución solo usan archivos sintéticos dentro de artefactos.

## Contratos y verificación

El estado de entrega se prueba con `TimeProvider`. IDs ajenos, respuestas huérfanas, completados duplicados y eventos tardíos se ignoran. Las versiones existentes de la extensión siguen funcionando. El contrato completo está en [BRIDGE_PROTOCOL.md](BRIDGE_PROTOCOL.md).

`scripts/verify.ps1` ejecuta ocho suites de lógica y transporte, las pruebas de distribución y la compilación Release. Usa salidas aisladas, se comparte con el hook `pre-push` y está configurado en GitHub Actions. El workflow se ha añadido al repositorio; su ejecución remota requiere subir estos cambios.

Las comprobaciones WinUI se ejecutan aparte con `scripts/check-tasks-ui.ps1`. Verifican controles reales, foco, temas, posiciones, borradores, almacenamiento aislado, conflictos, destinos y cierre. No requieren inspección visual ni una conversación o cuenta real.

La auditoría original queda como registro histórico. Se han abordado A1–A8 mediante aislamiento, composición, coordinadores, cierre controlado, persistencia atómica, índice, protocolo comprobable y distribución con rollback. La UI programática y el historial completo se mantienen intencionadamente; no se ha convertido la aplicación entera a MVVM ni se ha cambiado su diseño.

Validación local final: ocho suites correctas, incluidos 34 casos de núcleo, 15 de puente, 104 de Gmail y 44 de extensión; pruebas sintéticas de instalación y rollback correctas; comprobaciones WinUI correctas; solución Release con cero errores y cero advertencias. La comparación estática con el estado inicial no detectó etiquetas visibles añadidas o eliminadas en la pantalla.

La versión publicada se activó desde Windows el 15 de octubre de 2026. Se verificaron el proceso de la publicación, el protocolo de diagnóstico y la conexión con la extensión, sin enviar mensajes de prueba a ChatGPT. Se actualizó el acceso directo de este proyecto al lanzador estable, guardando previamente una copia. Los resultados reproducibles están en `artifacts/architecture-refactor/final-verification.log`, `final-winui.log` y `activation-result.json`.
