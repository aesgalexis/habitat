# Estado verificable del proyecto

Revisión documental: **24 de octubre de 2026**. Base local: `4faa5e9`, con cambios sin commit y otra implementación en curso. Este documento registra una inspección estática; no se han ejecutado las suites de producto durante esta revisión ni se han verificado las tiendas o publicaciones remotas.

## Cómo interpretar el estado

- **Presente:** hay código en esta copia. No implica que esté publicado ni que se haya vuelto a probar hoy.
- **Declarado:** un archivo de versión, manifiesto o changelog indica un valor; no acredita los bytes de una publicación remota ni la versión instalada.
- **Pendiente de reconciliar:** existe documentación anterior, pero faltan componentes citados o contradice esta copia. Puede pertenecer a otra rama; no se presupone su origen.
- **Histórico:** evidencia de una fecha y revisión concretas, con sus límites originales.

## Código y metadatos

| Área | Estado y evidencia local |
| --- | --- |
| Escritorio | WinUI 3 y .NET 10; [proyecto de la app](../src/Habitat.App/Habitat.App.csproj) y [biblioteca compartida](../src/Habitat.Core/Habitat.Core.csproj). |
| Destinos | OpenAI, Google, YouTube, Notepad, Translate, Gmail, Google Maps, Codex, VS Code, Terminal y Run; [catálogo](../src/Habitat.Core/Services/ComposerDestination.cs). |
| Preferencias | Appearance, Location, ChatGPT y Shortcuts; [vista](../src/Habitat.App/Features/Preferences/PreferencesView.cs). Interfaz en inglés. |
| Persistencia | Tareas, preferencias y registro de chats de origen; [almacenamiento](../src/Habitat.Core/Services/Storage), [registro Codex](../src/Habitat.Core/Services/Codex/CodexChatOrigins.cs). |
| Integraciones | Puente Chrome, Gmail, tiempo, atajo global y terminal VS Code; [arquitectura](ARCHITECTURE.md). |
| Actualizaciones | Código presente y en modificación local; [servicio](../src/Habitat.App/Services/DesktopUpdateService.cs), [flujo](UPDATES.md). |
| Facebook | Integración añadida durante el trabajo simultáneo; [guía](FACEBOOK.md) y [servicio de contactos](../src/Habitat.Core/Features/Facebook/FacebookContactsService.cs). No se ha verificado ni probado como parte de esta revisión documental. |
| Escritorio declarado | [VERSION](../VERSION) y [release.json](../release.json) indican 0.16.1 en esta revisión. No se ha comprobado su publicación. |
| Extensión Chrome | El [manifiesto fuente](../extension/manifest.json) declara 0.10.0; `release.json` registra 0.9.0. Se distinguen desarrollo y metadatos de paquete, sin asumir que sean una misma entrega. |
| Extensión VS Code | El [manifiesto fuente](../vscode-extension/package.json) declara 0.2.0. |

## Documentación que requiere reconciliación

| Tema | Desajuste comprobado el 24 de octubre |
| --- | --- |
| Markets y sidebar configurable | Las guías citan `MarketsService`, `MainPage.Markets`, `SidebarCatalog` y `MainPage.Sidebar`; no se encontraron en `src`. Se conservan como referencias pendientes. |
| Programación de mensajes Codex | La guía cita `CodexScheduleQueue` y comprobaciones de programación ausentes en esta copia. No se documenta como acción disponible. |
| Fondos ilustrados | Las guías describen importación, biblioteca y `Assets/Wallpapers`, ausentes en esta copia. |
| Interfaz bilingüe | No se encontraron los catálogos ni `UiText`/`UiLanguage` citados anteriormente. Además, la regla vigente exige inglés; véase [LOCALIZATION.md](LOCALIZATION.md). |
| Funciones de Codex descritas en el README anterior | La selección persistente independiente de la navegación, **New chat** y la cabecera de modelo/razonamiento no quedan respaldadas por la sección y servicios actuales. La guía de uso describe únicamente el recorrido presente. |

No elimines trabajo de otra rama para resolver estos desajustes. Cuando se integre una implementación, revisa código, pruebas y etiquetas, actualiza su guía y retira el aviso pendiente solo con evidencia.

## Publicación y mantenimiento

La versión instalada se consulta en HABITAT; la versión del código se obtiene de sus manifiestos; el contenido publicado se comprueba contra los assets y metadatos de la release correspondiente. Son tres comprobaciones distintas.

El estado de Chrome Web Store y WinGet de sus guías es una observación fechada, no una consulta en directo. [SIGNING.md](SIGNING.md) conserva la decisión vigente de aplazar la firma. Para publicar, sigue [RELEASING.md](RELEASING.md).

Actualiza esta página cuando cambien los desajustes o se cierre una reconciliación; no hace falta editarla por cada corrección menor. Los detalles de cada entrega pertenecen al [changelog](../CHANGELOG.md).
