# Changelog

## 0.9.0 — 2026-10-18

Primera publicación versionada del escritorio WinUI 3 / .NET 10 para Windows x64.

- ChatGPT en la sesión de Chrome, con reconexión y protección del chat de una respuesta pendiente.
- Destinos OpenAI, Google, YouTube, Notepad, Translate, Gmail, Google Maps, Codex, VS Code, Terminal y Run.
- Borradores compartidos, resultados integrados y conservación de ediciones durante operaciones pendientes.
- Atajo global Ctrl+H, selección verificada, protección del portapapeles y confirmación de sustitución de borradores.
- Preferencias de apariencia y navegador, previsión del tiempo, Gmail con credenciales de Windows y borradores sin envío.
- Tareas persistentes con historial recuperable y protección frente a sobrescrituras de sesiones antiguas.
- Coordinación del editor separada de WinUI, cierre con cancelación y drenaje, cachés acotadas y servicios agrupados por integración.
- Instalador por usuario, paquete autocontenido, manifiesto completo, SHA-256 y actualización con recuperación de la versión anterior.
- Instalador EXE con Inno Setup, instalación/desinstalación silenciosas y generador de manifiestos candidatos para WinGet; la inclusión en el catálogo público queda pendiente de publicar una descarga pública y enviarla a revisión.
- Criterio SemVer, versión de ensamblados centralizada y publicación automatizada al etiquetar una versión.
- Corregida la prueba de VS Code en clones limpios: crea su carpeta de artifacts antes de generar datos temporales.

Incluye browser bridge **0.8.0** y HABITAT Terminal para VS Code **0.2.0**. Los cambios anteriores a esta publicación se conservan en el historial de Git.
