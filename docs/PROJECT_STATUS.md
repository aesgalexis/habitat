# Estado verificable del proyecto

Revisión: **6 de noviembre de 2026**. La versión fuente **0.20.0** añade [sincronización opcional de Tasks y carpeta HABITAT en Google Drive](TASKS_DRIVE_SYNC.md), conservando la agenda manual y los chats programados ampliados de 0.19.0. Contacts y Tasks comparten autorización con activación independiente; las tareas empiezan sin sincronización. Las comprobaciones usan dos equipos simulados y almacenamiento aislado. La prueba de dos equipos físicos queda para la activación en ambos PCs.
## Recuperación de las funciones consolidadas

La rama de Facebook partía de 4faa5e9 (0.10.0). Cambiar a esa rama sin integrar main dejó fuera 16 commits posteriores. El trabajo seguía guardado en Git y se ha fusionado conservando ambas líneas de historial. Véase [RECOVERY_2026-10-25.md](RECOVERY_2026-10-25.md).

| Área | Código presente |
| --- | --- |
| Fondos | Importación, biblioteca, ajustes por imagen y cinco ilustraciones en [Assets/Wallpapers](../src/Habitat.App/Assets/Wallpapers); [guía](WALLPAPERS.md). |
| Codex | Chat vinculado persistente, New chat, modelo y razonamiento; [servicio](../src/Habitat.Core/Services/Codex/CodexChatService.cs). |
| Programación | Cola persistente, mensajes para chats nuevos o existentes, indicadores y reanudaciones tras límites; [guía](CODEX_SCHEDULING.md). |
| Widgets | Mercados y barra lateral configurable; [Markets](MARKETS.md), [Sidebar](SIDEBAR.md). Facebook permanece accesible al personalizar los demás widgets. |
| Destinos | Selección, orden y aplicaciones personales; [catálogo](../src/Habitat.Core/Services/DestinationCatalog.cs). |
| Lectura y ventana | Desvanecido de bordes, recuperación del foco y estabilidad de minimizar/restaurar. |
| Actualizaciones | Modal centrado, Later, progreso y reintento. Conserva borradores al reiniciar y espera los envíos de Codex en curso; [flujo](UPDATES.md). |
| Contactos | [Destinos manuales](CONTACTS.md) persistentes, agenda inicialmente vacía, alta/edición/eliminación local y una ficha por persona con varios canales. Bloqueo de duplicados y de observaciones antiguas del navegador. |
| Facebook | El canal Messenger conserva verificación, borradores aislados, diagnóstico local y envíos explícitos. La ficha unificada permite seleccionarlo sin enviar nada; [evidencia previa y límites](FACEBOOK.md). |
| Idioma | Interfaz en inglés; contenido del usuario intacto; [política](LOCALIZATION.md). |

## Distribución y decisiones

La versión fuente declara **0.20.0**, la extensión de Chrome **0.10.9** y la de VS Code **0.2.0**. Las publicaciones se consultan en [GitHub Releases](https://github.com/aesgalexis/habitat/releases). Los scripts generan un release.json con el commit exacto de la etiqueta y verifican ambas instalaciones en CI. La versión instalada y las publicaciones son comprobaciones independientes. Las guías de Chrome Web Store y WinGet describen observaciones fechadas. La firma sigue aplazada conforme a [SIGNING.md](SIGNING.md).

Las comprobaciones de esta recuperación se registran en [RECOVERY_2026-10-25.md](RECOVERY_2026-10-25.md), sin atribuir a este árbol validaciones de otras revisiones.
