# Changelog

## 0.14.1 — 2026-10-21

- Refuerzo de la recuperación de entrada al activar HABITAT, recibir el foco o usar su canal de activación: se retira el modo de paso al escritorio sin depender exclusivamente de `WM_ACTIVATEAPP`.
- Pruebas nativas para el foco sin otra notificación de aplicación, activaciones repetidas y conservación de los estilos originales. Documentado el diagnóstico de la ventana que parecía congelada, con captura del proceso real y recuperación sin reinicio.

## 0.14.0 — 2026-10-21

- Esta publicación reúne también la interfaz en inglés/español, los fondos personalizables y las cinco ilustraciones incorporados desde la última versión publicada, 0.11.0.
- Corregido el cierre al minimizar/restaurar: Windows puede devolver un margen negativo para los botones de ventana; se normaliza antes de ajustar el título y se agrupan los cambios fuera de la notificación nativa. Se evitan también las actualizaciones repetidas del mismo título durante el reposo.
- Widget Mercados debajo de Tareas: favoritos, cotización y variación diaria, selección fija o rotación y panel con gráfica mensual y titulares enlazados a su fuente.
- Preferencias de Mercados en inglés y español; actualización periódica, conservación del último dato ante errores y pregunta preparada en el editor sin sustituir el borrador ni enviarlo.
- Pruebas con datos sintéticos y almacenamiento aislado para cálculos, persistencia, fallos de red, cancelación y controles WinUI.

## 0.13.1 — 2026-10-20

- Corregido el menú junto a minimizar: zona interactiva nativa ajustada al tamaño y escala de la ventana, y foco conservado mientras está abierto. Pruebas de apertura de Preferencias y retorno al editor en inglés y español.
- Cinco fondos ilustrados incluidos: Grafito (paisaje a lápiz), Dunas, Marea, Olivo y Órbita.
- Selección localizada, encuadre y atenuación para las ilustraciones, disponibles sin conexión. Se mantiene el fondo predeterminado transparente Híbrido al 15 %.

## 0.13.0 — 2026-10-20

- Fondos globales desde Preferencias → Apariencia: tres fondos de color, biblioteca de JPG/PNG propios, vista previa, encuadre y atenuación.
- Importación local con copia independiente, orientación EXIF y tamaño limitado. Híbrido adapta el contraste a la imagen elegida.
- El valor inicial sigue siendo el fondo transparente Híbrido al 15 %. Un botón restaura ese aspecto y conserva las imágenes guardadas.
- Comprobaciones de persistencia, errores de importación, recuperación y controles en inglés/español con almacenamiento aislado.

## 0.12.0 — 2026-10-20

- Al perder el foco, HABITAT deja pasar clics y arrastres al escritorio y a las ventanas inferiores. La barra de tareas, Alt+Tab y Ctrl+H permiten recuperar la interacción; los menús y diálogos propios siguen funcionando.
- Interfaz de escritorio en inglés y español, con selector en Preferences → Appearance. El idioma se guarda y se aplica al cerrar el diálogo; los perfiles anteriores conservan inglés.
- Catálogos compartidos para textos visibles, accesibilidad, carga, errores y recuperación. Las etiquetas traducidas se separan de los IDs de categorías, destinos y protocolos.
- El cambio conserva borradores, tareas, resultados y traducciones pendientes. Fechas y cantidades visibles siguen la cultura de interfaz sin modificar formatos de intercambio ni el contenido del usuario.
- Comprobaciones automáticas de catálogos, persistencia y cambio de idioma con controles WinUI y almacenamiento aislado.

## 0.11.0 — 2026-10-20

- Aviso automático de nuevas versiones: consulta GitHub al abrir HABITAT y cada seis horas mientras está abierto. El icono **Update HABITAT**, al final del sidebar, muestra la versión disponible y abre su página oficial para descargar el EXE o ZIP correspondiente. La descarga y la instalación siguen siendo manuales.
- La comprobación ignora versiones anteriores, borradores, betas y publicaciones sin instalador Windows terminado. Los fallos de conexión conservan el aviso conocido y se reintentan tras una hora; las consultas pendientes se cancelan al cerrar.
- **Preferences… → Destinations** permite añadir, quitar y ordenar los destinos, elegir uno al arrancar o recordar el último. Quitar un destino conserva su configuración y el contenido de la sesión; los antiguos perfiles migran a identificadores estables.
- Destinos para aplicaciones de Windows, incluidas las de Microsoft Store, con selección de ejecutables y accesos directos. Por defecto abren la aplicación y conservan el borrador; los ejecutables compatibles pueden recibir el texto como un único argumento literal.
- Los destinos en reposo muestran **Ready** desde el arranque. La conexión, el progreso y los errores de ChatGPT permanecen en OpenAI y sus respuestas en segundo plano no sustituyen el estado de otro destino.
- Pruebas con versiones sintéticas y almacenamiento aislado para comprobar intervalos, fallos de red, cancelación, accesibilidad, ambos temas, posición del icono y espacio reservado para Tasks. Las extensiones mantienen sus versiones: Chrome **0.9.0** y VS Code **0.2.0**.

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
