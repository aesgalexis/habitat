# Estado verificable del proyecto

Revisión: **25 de octubre de 2026**. La rama de recuperación integra main hasta 1c4138a (0.16.1) y la implementación de Facebook e5a2ecc, incluidos los cambios locales de la extensión 0.10.3. No implica una nueva publicación remota.

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
| Facebook | Extensión local 0.10.3 y contactos cargados en Chrome; [evidencia previa y límites](FACEBOOK.md). La recuperación no vuelve a leer contactos reales durante las pruebas. |
| Idioma | Interfaz en inglés; contenido del usuario intacto; [política](LOCALIZATION.md). |

## Distribución y decisiones

La versión fuente sigue declarando 0.16.1; la extensión local declara 0.10.3. La versión instalada y las publicaciones son comprobaciones independientes. Las guías de Chrome Web Store y WinGet describen observaciones fechadas. La firma sigue aplazada conforme a [SIGNING.md](SIGNING.md).

Las comprobaciones de esta recuperación se registran en [RECOVERY_2026-10-25.md](RECOVERY_2026-10-25.md), sin atribuir a este árbol validaciones de otras revisiones.
