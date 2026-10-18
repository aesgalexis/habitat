# Auditoría de arquitectura — 18 de octubre de 2026

Base revisada: `113b708`, con el árbol de trabajo limpio al comenzar. Esta revisión complementa la [auditoría del 15 de octubre](ARCHITECTURE_AUDIT.md), que se conserva como registro histórico.

## Conclusión

La aplicación tiene una base razonable para seguir creciendo: hay una composición explícita de servicios, lógica comprobable fuera de WinUI, adaptadores de integración y pruebas aisladas. No se han encontrado dependencias de proyecto circulares. El crecimiento reciente ha añadido secciones independientes en vez de introducir todos los destinos dentro del código de construcción de la página.

La frontera que más atención necesita sigue siendo `MainPage`: sus parciales de producción suman unas 2.650 líneas y comparten estado mutable. Separar archivos facilita leerlos, pero no crea límites entre responsabilidades. La corrección de VS Code de esta revisión muestra una extracción útil: mover las reglas de selección a un objeto sin controles, conservando la vista y sus pruebas nativas.

Se han corregido cuatro problemas. Las pruebas existentes pasaban antes de añadir los nuevos casos; por tanto, esta auditoría no se limita a recompilar ni considera la suite anterior una garantía de ausencia de errores.

## Problemas corregidos

### 1. VS Code dejaba de seguir el proyecto activo tras un envío — Alta

**Causa:** `VSCodeSection.ResolveProjectAsync` establecía siempre `_followActive = false`, incluso cuando el usuario no había elegido una carpeta manualmente. Después del primer comando, activar otro proyecto en VS Code podía dejar HABITAT preparado para ejecutar en el anterior.

**Corrección:** las reglas viven ahora en [VSCodeProjectSelection](../src/Habitat.Core/Features/VSCode/VSCodeProjectSelection.cs). Resolver la conexión de un proyecto conserva la política de selección. Solo seleccionar un proyecto o una carpeta explícitamente fija la ruta. El comando pendiente conserva la carpeta capturada antes de esperar la conexión.

**Cobertura:** seguimiento automático después de resolver y enviar, selección manual persistente, desconexión sin conservar un endpoint antiguo, reconexión de la misma carpeta y selector de carpetas. La comprobación WinUI reproduce un envío, cambia la ventana activa y verifica también que la selección explícita sigue fija.

### 2. Un paquete JSON inesperado podía inutilizar el lector de Codex — Media

**Causa:** `CodexReadClient.ReadLoopAsync` utilizaba `TryGetProperty` sin comprobar que la raíz era un objeto. Un array o `null` provocaba una excepción fuera de su manejo de desconexión. La tarea quedaba fallida y podía propagar el error también durante el cierre. Además, una petición nueva podía reutilizar el proceso que ya estaba fallando antes de que Windows notificara su salida.

**Corrección:** se valida el tipo de paquete y se marca el lector como fallido antes de completar las solicitudes pendientes. La reconexión espera la finalización del lector anterior y de su drenaje de diagnósticos antes de crear otro proceso privado. No se reintentan envíos; esta conexión sigue limitada a consultas de historial.

**Cobertura:** proceso sintético que devuelve un array, `null` o JSON incompleto, seguido inmediatamente de otra consulta válida. La prueba original falló con `InvalidOperationException`; las pruebas ampliadas detectaron también la carrera de reconexión y pasan con la corrección completa.

### 3. Cerrar durante la apertura de Codex podía propagar una cancelación — Media

**Causa:** `CodexSection.OpenAsync` filtraba su `catch` con `when (!_disposed)`. Al cerrar la sección, la cancelación quedaba fuera de ese manejador y podía propagarse al evento `async void` del botón. También se podía intentar abrir desde una sección ya liberada.

**Corrección:** se rechazan nuevas aperturas después del cierre y se absorbe la cancelación de la operación al liberar la sección. Los errores de navegación posteriores al cierre tampoco modifican controles ni escapan del evento.

**Cobertura:** servicio sintético que espera hasta recibir cancelación; cierre durante la apertura; nueva apertura tras liberar la sección. No se abre Codex real.

### 4. Los diagnósticos del proceso auxiliar se acumulaban en memoria — Baja

**Causa:** `StandardError.ReadToEndAsync` almacenaba toda la salida de error hasta terminar el proceso auxiliar. Aunque el resultado no se usaba, la memoria podía crecer durante una sesión larga.

**Corrección:** descarte continuo mediante un buffer fijo de 4.096 caracteres y tarea de drenaje conservada y esperada al reconectar y cerrar. No se registran los diagnósticos ni el contenido de conversaciones.

**Cobertura:** proceso sintético que produce más de 512 KiB de diagnósticos antes de responder. Se verifica que la consulta y el cierre terminan; el límite de memoria se establece por la implementación del buffer, sin afirmar una medición del consumo de la aplicación completa.

## Arquitectura comprobada

| Frontera | Evaluación |
| --- | --- |
| `AppServices` → servicios y adaptadores | Un punto de composición con infraestructura aislada para pruebas. Mantener esa propiedad al añadir funcionalidades. |
| `Habitat.App` → `Habitat.Core` | Dependencia de proyecto en una dirección; las pruebas usan la misma biblioteca. Core también contiene infraestructura Windows y HTTP, no solo dominio puro. |
| Destinos integrados | `IDestinationSection` evita concentrar todas las vistas en la página. Las reglas que no necesitan controles deben seguir saliendo hacia `Core/Features`. |
| Tareas | Cambios serializados fuera del hilo de UI, detección de conflictos, escritura atómica e historial recuperable. El índice conserva las revisiones. |
| Gmail | Sesión separada, acceso de lectura, persistencia protegida y clientes inyectables. Las pruebas usan cuentas y mensajes sintéticos. |
| Puentes de navegador y VS Code | Autenticación local, correlación por ID, separación de proveedores y pruebas de cancelación, resultados tardíos y entregas duplicadas. |
| Distribución | Preparación independiente, manifiestos y pruebas de rollback. La auditoría no activa ninguna publicación. |
| Verificación | Un comando común para las siete suites .NET, cinco suites JavaScript, distribución y compilación; controles WinUI comprobados por separado. |

## Deuda identificada y resolución posterior

Estas observaciones son riesgos de mantenimiento o rendimiento inferidos por inspección; no son nuevos fallos reproducidos en los datos del usuario.

Tras la autorización del usuario se han abordado los cuatro puntos siguientes. Se conservan las observaciones originales para distinguir lo detectado de la implementación final.

1. **Media — reducir la coordinación de `MainPage`.** El archivo principal tiene 1.055 líneas y el conjunto de parciales de producción unas 2.650. `Timer_Tick`, el enrutamiento de destinos, las preferencias y la presentación comparten la clase. `ComposerDrafts` ya separa los borradores; la siguiente extracción debería separar la coordinación de envíos y su disponibilidad, con pruebas sin WinUI y manteniendo las comprobaciones nativas. Dividir más archivos o introducir un contenedor de DI no resolvería ese acoplamiento por sí solo.
2. **Media — unificar la finalización asíncrona de secciones.** La página drena sus operaciones, pero `IDestinationSection` solo exige `IDisposable`; algunas consultas periódicas y acciones propias de las secciones se cancelan sin un contrato común para esperarlas. Conviene introducir una parada asíncrona cuando se amplíe esa infraestructura, probando cierre durante lectura y navegación antes de liberar los clientes compartidos. La cancelación concreta que podía escapar en Codex queda corregida en esta revisión.
3. **Baja — acotar cachés de sesión con una política explícita.** `CodexChatSession` limita cada lectura a veinte turnos, pero sus diccionarios de chats visitados y textos originales crecen durante la sesión. `ConversationSession` también conserva todo su historial. Medir con datos sintéticos y definir expulsión de chats inactivos, preservando el chat seleccionado y cualquier respuesta pendiente; no aplicar limpieza a las revisiones de tareas.
4. **Baja — ordenar infraestructura por integración cuando crezca.** `Core/Services` contiene desde reglas puras hasta P/Invoke, OAuth y transporte. No requiere crear más ensamblados inmediatamente. Evitar que nuevas reglas se añadan a esos adaptadores; agrupar funcionalidades en `Features` y mantener las dependencias Windows identificadas y probadas con dobles.

### Implementación de la deuda priorizada

| Punto | Cambio y verificación |
| --- | --- |
| Coordinación de página | `ComposerCoordinator` separa admisión, rutas, exclusión y revisiones del borrador; `ConversationView` posee los controles de conversación. Pruebas sin WinUI verifican duplicados, fallos, rutas integradas/externas, confirmación pendiente durante el cierre y conservación de ediciones posteriores. La página sigue siendo la vista principal; esto no implica una migración completa a MVVM. |
| Parada de secciones | Contrato común `StopAsync`, tareas de proveedores y navegación registradas, cancelación inmediata de la espera visual y drenaje antes de liberar servicios. Pruebas nativas cubren búsqueda, traducción, descubrimiento de VS Code y lectura, envío y apertura de Codex con proveedores sintéticos que responden tarde. |
| Cachés | Codex: 32 chats y veinte turnos, con selección y respuesta pendiente protegidas; se expulsan conjuntamente historial, avisos y originales antiguos. OpenAI: 200 mensajes y presupuesto de 2.000.000 de caracteres, conservando íntegro el último mensaje y avisando de la disponibilidad del historial en ChatGPT. Pruebas sintéticas fuerzan ambos límites y comprueban que se retiran también controles nativos. |
| Infraestructura | Adaptadores agrupados en siete carpetas de integración, sin nuevos ensamblados, dependencias ni cambios de namespace. Los proyectos y suites compilan los mismos archivos de producción. |

La limpieza de búsqueda al editar utiliza `TextChanging`, junto con la revisión del borrador. Así la decisión se toma en la edición que la provoca y evita depender de eventos diferidos de `TextChanged` al restaurar un modo. Se mantienen el reinicio inmediato de Google y los 750 ms de YouTube.

Los cambios simultáneos del widget de tiempo se conservan. Su distribución no forma parte de la refactorización arquitectónica; las pruebas finales verifican el árbol de trabajo combinado. El script WinUI admite una carpeta de salida dentro de `artifacts` para separar ejecutable, datos y registros entre verificaciones.

## Verificación y límites

La verificación completa de partida pasó, incluida la solución Release sin advertencias. Se añadieron pruebas de regresión y se ejecutaron después las suites de lógica, transporte y controles nativos con almacenamiento explícito bajo `artifacts`. Los registros de esta revisión están en `artifacts/audit-2026-10-18`.

| Verificación final | Resultado |
| --- | --- |
| `scripts/verify.ps1 -NoRestore` | Correcta: siete suites .NET, cinco suites JavaScript, sintaxis, manifiesto y pruebas de distribución. |
| Solución Release, x64, `HabitatUiChecks=false` | Correcta: cero errores y cero advertencias; salida aislada. |
| `scripts/check-tasks-ui.ps1` | Correcta: todos los destinos, los nuevos casos de VS Code y Codex, tareas, borradores, temas y cierre con servicios sintéticos. |
| `git diff --check` | Correcta. |

La restauración de dependencias también se completó. Las restricciones iniciales de red del sandbox se resolvieron ejecutando las pruebas que requieren conexiones locales fuera de él; no se modificaron las pruebas para ocultar esos fallos de entorno.

Las pruebas no leen ni modifican las tareas, el buzón o las conversaciones reales. No se ejecutan comandos en proyectos reales de VS Code ni pruebas de envío a ChatGPT/Codex. La suite WinUI comprueba controles directamente con servicios sintéticos; no se han usado capturas ni control visual. No se ha medido una sesión prolongada, certificado el DOM actual de los proveedores externos ni realizado una auditoría exhaustiva de vulnerabilidades o compatibilidad entre versiones de Windows.

## Validación de la refactorización y activación

El seguimiento autorizado de la auditoría ha terminado con las siete suites .NET, las cinco suites JavaScript, distribución y controles WinUI correctos. La compilación Release x64 terminó con cero errores y cero advertencias. Los 173 archivos de código, pruebas y scripts conservaron sus huellas entre la validación y la publicación. Las pruebas nativas se publicaron en una carpeta exclusiva dentro de `artifacts/debt-2026-10-18` para no compartir ejecutable con las comprobaciones simultáneas del widget de tiempo.

La publicación se activó el 18 de octubre de 2026 a las 11:47, desde la sesión normal de Windows y sin perfil temporal. El bloqueo de actualización impidió inicialmente solaparla con otra instalación; se esperó a su finalización antes de activar la publicación verificada. Se comprobó el proceso activo, el protocolo local, la conexión de la extensión y la huella de cada archivo publicado contra el manifiesto. La versión anterior se conserva. No se enviaron mensajes de prueba.

El registro `gmail-startup.log` se genera al iniciar la aplicación y no forma parte del manifiesto preparado. La verificación posterior al arranque comprobó todos los archivos publicados y distinguió ese diagnóstico adicional, sin leer su contenido ni alterar el manifiesto.

Evidencia reproducible: `artifacts/debt-2026-10-18/verification.log`, `ui.log`, `publish.log`, las instantáneas `source-before-validation.json` y `source-after-validation.json`, y `activation-result.json`. Este último confirma la publicación `release-be4d4cf3922149288054ab3f602f32c1`, arranque e integridad correctos y extensión conectada.

Después de esta publicación se detectaron nuevas ediciones simultáneas del widget de tiempo en `MainPage.xaml.cs`, `MainPage.Tasks.cs`, `MainPage.Weather.cs` y las comprobaciones de tareas. Se conservan en el árbol de trabajo; esas ediciones posteriores no forman parte de la publicación ni de la validación aquí registrada. La evidencia corresponde a la instantánea con huellas guardada, no a cualquier modificación futura de los archivos compartidos.
