# Estado verificable del proyecto

Revisión: **27 de octubre de 2026**. El código conserva las funciones consolidadas de 0.16.1 y la integración de Facebook, con la extensión 0.10.7. La versión 0.16.4 añade contactos con presencia pasiva y corrige la preparación del actualizador con rutas de módulos heredadas.

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
| Facebook | Extensión local 0.10.7, widget **Contacts**, tarjetas sin tooltips, tamaño en Preferencias y presencia explícita consultada al abrir o reintentar; [evidencia previa y límites](FACEBOOK.md). El contacto activo se muestra junto al sidebar y queda fuera de la barra derecha. Envío real confirmado previamente por el propietario; pestañas temporales, canal Facebook, pruebas sintéticas y diagnóstico local sin escritura. |
| Idioma | Interfaz en inglés; contenido del usuario intacto; [política](LOCALIZATION.md). |

## Distribución y decisiones

La versión fuente declara **0.16.4**, la extensión de Chrome **0.10.7** y la de VS Code **0.2.0**. La etiqueta y los paquetes se consultan en la [publicación 0.16.4](https://github.com/aesgalexis/habitat/releases/tag/v0.16.4). Los scripts de distribución generan un release.json con el commit exacto de la etiqueta y verifican ambas instalaciones en CI. La versión instalada y las publicaciones son comprobaciones independientes. Las guías de Chrome Web Store y WinGet describen observaciones fechadas. La firma sigue aplazada conforme a [SIGNING.md](SIGNING.md).

Las comprobaciones de esta recuperación se registran en [RECOVERY_2026-10-25.md](RECOVERY_2026-10-25.md), sin atribuir a este árbol validaciones de otras revisiones.
