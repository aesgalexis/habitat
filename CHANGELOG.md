# Changelog

## 0.10.0 — 2026-10-19

- Todos los destinos web se abren en Google Chrome. Eliminado el selector de navegador y corregida la desconexión al usar el navegador predeterminado de Windows; las preferencias anteriores conservan los demás ajustes.
- Guía inicial **Set up HABITAT…**, disponible también en el menú, con detección de Chrome, acceso a la extensión de la tienda, alternativa descomprimida y estado independiente de las cuatro conexiones.
- Instrucciones completas para instalar en otro PC, conectar ChatGPT y configurar las integraciones opcionales de Gmail, VS Code y Codex.
- Browser bridge **0.9.0** con consentimiento explícito antes de conectar, pantalla de bienvenida, desconexión de todos los canales y política de privacidad. Enviada a Chrome Web Store como Unlisted; pendiente de aprobación de Google al preparar esta versión.
- EXE y ZIP incluyen los mismos recursos de la extensión, iconos y privacidad, sin utilidades de desarrollo. Ambos formatos identifican el commit fuente y las versiones de las extensiones.
- YouTube reconoce canales coincidentes con la consulta y mejora la presentación de miniaturas y títulos; se conservan bloques de 20 vídeos.
- Verificación del asistente con almacenamiento aislado y comprobación de los paquetes, sus recursos y sus runtimes autocontenidos.
- El cierre de diálogos y las respuestas pendientes conservan el foco del campo de Tasks mientras su panel está abierto.

## 0.9.1 — 2026-10-18

Primera distribución instalable del escritorio WinUI 3 / .NET 10, con todas las funciones descritas en 0.9.0, ZIP y EXE autocontenidos, extensiones y SHA-256.

- Corregida la comprobación del changelog para aceptar los finales de línea CRLF de un clon nuevo de Windows.
- Conservada la etiqueta histórica `v0.9.0`, cuya publicación automatizada se detuvo antes de distribuir paquetes. Esta corrección incrementa PATCH y mantiene las etiquetas inmutables.

## 0.9.0 — 2026-10-18

Primera instantánea versionada del escritorio WinUI 3 / .NET 10 para Windows x64. La publicación de paquetes se detuvo por la comprobación CRLF corregida en 0.9.1.

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
