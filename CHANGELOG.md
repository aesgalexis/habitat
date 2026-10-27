# Changelog

## 0.17.1 — 2026-10-27

- Primera distribución instalable de la agenda unificada: fichas persistentes, procedencia de cada dato, fusiones manuales y sugeridas con confirmación y deshacer, y acceso a Gmail o Messenger cuando el contacto y la integración lo permiten.
- La extensión de Chrome 0.10.8 incorpora fuentes opcionales automáticas desde Facebook, Gmail y Google Contacts abiertos. Se leen las personas cargadas con permisos concedidos; Gmail aporta corresponsales visibles sin asuntos ni cuerpos. Se incluyen la compatibilidad de los ZIP anteriores y los diagnósticos persistentes del actualizador.
- El auxiliar del actualizador guarda la fase de espera antes de señalar que está preparado. Se elimina la condición de carrera detectada en CI entre esa señal y la lectura de sus diagnósticos, manteniendo la aplicación abierta hasta verificar la preparación.
- Se conserva `v0.17.0` como etiqueta histórica sin paquetes publicados; las etiquetas ya subidas no se modifican.

## 0.17.0 — 2026-10-27

- Agenda unificada persistente con fichas por persona, datos y versiones anteriores conservados por fuente, búsqueda por nombre/correo/teléfono y acceso a los canales compatibles.
- Fuentes automáticas en **Preferences → Contacts**: Facebook, Gmail y Google Contacts aportan las personas cargadas en pestañas abiertas, sin vistas previas ni importaciones repetidas. Se reutilizan permisos existentes de Chrome; el acceso nuevo se concede una vez. Desactivar conserva la agenda y bloquea las observaciones pendientes.
- La extensión 0.10.8 lee solo nombres, direcciones y datos de las filas compatibles: Gmail aporta corresponsales visibles, sin asuntos ni cuerpos; Google Contacts y Facebook aportan sus listas cargadas. No se promete una agenda completa ni se abren o desplazan páginas para la lectura automática.
- Fusiones manuales y sugerencias por correo, teléfono o enlace de perfil de Facebook, con comparación, confirmación, deshacer y rechazo recordado. Las coincidencias entre fuentes no se fusionan automáticamente.
- Las fichas abren borradores en Gmail con cuenta remitente elegida o permiten seleccionar Messenger con verificación del destinatario original. Se conservan los borradores; iMessage y otros canales requieren una integración compatible. Importación vCard y acceso completo por Google API siguen disponibles como opciones adicionales.
- ZIP de distribución con separadores portables y lectura compatible con archivos anteriores de Windows PowerShell. Diagnósticos persistentes y redactados para preparación, auxiliar, activación y recuperación del actualizador; se conserva la protección de rutas y hashes.
- Pruebas sintéticas de sincronización automática, permisos, identidad, procedencia, almacenamiento, fusiones, paginación, controles WinUI y migración del actualizador. Ninguna prueba de publicación envía mensajes reales ni utiliza los contactos del usuario.

## 0.16.4 — 2026-10-27

- El widget de contactos se llama **Contacts**, con etiquetas accesibles en inglés. El destinatario seleccionado queda junto al sidebar incluso en ventanas anchas y ya no ocupa un destino fijo en la barra derecha.
- La lista y la presencia se leen al abrir Contacts o al reintentar tras un error. Se eliminan los refrescos periódicos y los reintentos automáticos que reconstruían las tarjetas cada treinta segundos.
- Corregida la preparación del actualizador cuando HABITAT hereda rutas de módulos de PowerShell 7 u otros hosts: ambos auxiliares usan los módulos de Windows PowerShell 5.1 y mantienen la aplicación abierta si la verificación falla.
- Pruebas aisladas de posición, navegación, borradores, presencia pasiva y preparación real del auxiliar con rutas de módulos incompatibles y hashes incorrectos.

## 0.16.3 — 2026-10-26

- Mensajería explícita a contactos de Facebook desde HABITAT, con identidad verificada, borradores separados por contacto y protección frente a envíos duplicados o inciertos.
- Seleccionar un contacto cierra su panel y mantiene su foto y nombre arriba a la izquierda, además del destino activo en la barra. La acción Send message muestra Facebook debajo para identificar el canal.
- Las pestañas temporales de envío y diagnóstico se cierran al terminar correctamente. Los borradores, las verificaciones de acceso y los envíos inciertos permanecen disponibles para revisión.
- Icono de contactos con el hover del sidebar, búsqueda local por nombre, tarjetas pequeñas por defecto, tamaño en Preferences y presencia online/offline cuando Facebook la indica explícitamente.
- Registro local acotado del ciclo de envío y comando de inspección que verifica la conversación y su editor sin insertar ni enviar texto.

## 0.16.2 — 2026-10-25

- Recuperadas las 16 revisiones consolidadas hasta 0.16.1 mediante fusión con la rama de Facebook: fondos e ilustraciones, mensajes programados de Codex, chat persistente, Mercados, sidebar configurable, destinos personales y mejoras de lectura y ventana.
- Conservados Facebook y las correcciones locales de la extensión 0.10.3, su barra de avatares y los avisos modales de actualización. La personalización del sidebar mantiene Facebook; la actualización conserva borradores y espera los envíos de Codex en curso.
- Interfaz en inglés conforme a la regla vigente; los perfiles bilingües conservan sus datos y usan las etiquetas inglesas.
- Corregida la preparación de paquetes bajo OneDrive: se reconocen sus metadatos Cloud Files y se mantiene el rechazo de enlaces, junctions y etiquetas desconocidas en todos los ancestros de las rutas de instalación.

## 0.16.1 — 2026-10-23

- Corregida la pérdida del chat de HABITAT al abrir otro chat en Codex: la conversación queda vinculada por su identificador y se recupera tras reiniciar, independientemente de la selección visible en Codex.
- Los mensajes vuelven a abrir y verifican el chat vinculado antes de enviarse, con protección de borradores, turnos en ejecución y cambios de destino durante la entrega.
- «Nuevo chat» permite empezar otra conversación sin borrar la anterior en Codex; limpia el vínculo y el borrador y devuelve el foco al editor inicial centrado. La selección vacía también se conserva tras reiniciar.
- La actualización recupera el chat registrado disponible de las versiones anteriores. Las entregas programadas mantienen el vínculo de la conversación actual.
- Comprobaciones de persistencia, navegación, reinicio y envío al destino correcto, junto con pruebas WinUI en inglés y español y documentación actualizada.

## 0.16.0 — 2026-10-23

- Desvanecido suave de 20 píxeles en los bordes con contenido oculto de las áreas de lectura. Respeta el fondo real, las barras de scroll, la selección y los controles nativos; se desactiva cuando el contenido cabe y se recupera al cambiar de vista.
- El chat de Codex muestra el modelo y la intensidad de razonamiento debajo del título como última configuración conocida, con valores ausentes explícitos y etiquetas en inglés y español.
- Las etiquetas de autor de Codex comparten los colores de OpenAI y se actualizan al cambiar de tema. Se elimina el aviso de turno incompleto cuando una lectura posterior confirma su finalización correcta.
- Preferencias permite añadir, retirar y ordenar Gmail, Tareas y Mercados en la barra lateral. Tiempo mantiene su posición; los perfiles existentes conservan sus widgets y los nuevos comienzan con Mercados desactivado.
- La programación de Codex permite seleccionar uno o varios chats detenidos por límite de uso, incluidos chats creados directamente en Codex. Cada reanudación queda vinculada a su turno interrumpido para evitar duplicados si el chat ya ha continuado.
- Pruebas de renderizado y ciclo de vida del scroll, configuración de Codex, reanudaciones programadas y personalización de la barra lateral, con datos aislados y ambos idiomas y temas.

## 0.15.6 — 2026-10-22

- Un envío confirmado por Codex se mantiene como enviado aunque falle la identificación posterior del chat, su registro local o el acceso al historial. Estas comprobaciones ya no generan avisos falsos de revisión ni reintentos de un mensaje entregado.
- Se reconoce el salto de línea final que Codex añade al guardar un mensaje, sin modificar el texto enviado ni relajar la comprobación del editor.
- Los mensajes enviados y cancelados quedan en un historial cerrado por defecto. La lista principal y los avisos se reservan para envíos pendientes o que requieren atención real.
- Pruebas de regresión para confirmación, fallos posteriores al envío, conservación de espacios, reinicios y ausencia de avisos para mensajes entregados.

## 0.15.5 — 2026-10-22

- El calendario de Codex muestra el número de mensajes pendientes y la hora del próximo envío en su tooltip y etiqueta accesible. El contador se recupera tras reiniciar y se actualiza al enviar, reprogramar o cancelar.
- Guardar muestra una confirmación temporal junto al calendario sin quitar el foco al editor. Con el editor vacío, abrir el calendario muestra primero la lista de programaciones existentes.
- Los envíos que requieren revisión y los errores de almacenamiento muestran una señal ámbar persistente. Los estados enviados o cancelados no cuentan como pendientes.
- Textos y comprobaciones en español e inglés, con ambos temas y datos de prueba aislados.

## 0.15.4 — 2026-10-22

- La programación permite elegir **Nuevo chat de Codex**, incluso antes de haber enviado el primer mensaje desde HABITAT. El chat se crea a la hora programada; guardar no envía nada.
- Los chats existentes creados desde HABITAT mantienen la prioridad por límite de uso. Si su listado falla, sigue siendo posible programar un chat nuevo sin cambiar el destino de una programación existente.
- La cola distingue de forma explícita un chat nuevo de un destino existente, conserva las programaciones anteriores y evita repetir un envío dudoso tras un reinicio.
- Un borrador en el editor de nuevo chat de Codex bloquea la entrega programada para conservarlo. Las vistas de Codex sin editor dejan de producir errores repetidos al consultar el chat visible.

## 0.15.2 — 2026-10-21

- Los mensajes programados para Codex solo permiten elegir chats creados desde HABITAT. Se elimina la opción de incluir otros chats locales.
- La entrega vuelve a comprobar el origen antes de leer o abrir el destino. Las programaciones externas guardadas por versiones anteriores quedan sin enviar y requieren revisión.
- Pruebas del filtro de origen, rechazo de destinos externos persistidos y diálogo en inglés y español.

## 0.15.1 — 2026-10-21

- Icono junto a «Enviar a Codex» para programar mensajes con chat de destino, fecha y hora locales; permite editar o cancelar los mensajes guardados.
- Prioridad para los chats cuyo último turno terminó por límite de uso y sugerencia de la hora de recuperación cuando Codex la expone. El selector permite incluir otros chats locales mediante una elección explícita.
- Cola persistente, espera de chats ocupados, protección de borradores y revisión manual de envíos dudosos o con más de cinco minutos de retraso. HABITAT debe permanecer abierto y Windows con la sesión iniciada.
- Conserva la actualización directa de 0.15.0 para comprobar desde otro PC la detección, descarga e instalación de esta versión.

## 0.15.0 — 2026-10-21

- El icono de actualización descarga el paquete oficial, verifica su SHA-256, instala y vuelve a abrir HABITAT. Conserva el formato EXE o ZIP de la instalación y los datos de usuario.
- Progreso y cancelación en inglés y español, espera a las acciones en curso, conservación de borradores al reiniciar y reintento ante errores de descarga o instalación.
- Las versiones anteriores necesitan instalar esta versión una vez; la actualización directa queda disponible para las siguientes versiones.

## 0.14.3 — 2026-10-21

- Cambio únicamente de versión para comprobar la detección automática de actualizaciones desde otro PC. Mismo comportamiento que 0.14.2.

## 0.14.2 — 2026-10-21

- Eliminado el paso automático de clics al escritorio. HABITAT mantiene el comportamiento de una ventana normal al perder el foco: hacer clic en la aplicación vuelve a activarla, sin necesitar Alt+Tab ni la barra de tareas.
- Prueba con la ventana WinUI real que verifica que 30 ciclos de desactivación y activación nunca habilitan `WS_EX_TRANSPARENT`, además de minimizar/restaurar, menú en ambos idiomas y actualización en reposo.

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
