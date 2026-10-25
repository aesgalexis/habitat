# HABITAT — WinUI 3

[English](README.md) | **Español**

[Descargar HABITAT para Windows x64](https://github.com/aesgalexis/habitat/releases/latest) · [Guía de primera instalación](docs/GETTING_STARTED.md) · [Historial de versiones](CHANGELOG.md)

Aplicación de escritorio nativa para Windows que permite escribir en el chat activo de ChatGPT en Chrome y recibir sus respuestas dentro de HABITAT. No crea chats separados, no rota conversaciones ni cambia sus nombres. Usa la sesión real de Chrome, sin consumir la API de OpenAI. Una respuesta en curso termina en su chat original antes de seguir el chat activo.

La aplicación actual está desarrollada en **C# y WinUI 3 sobre .NET 10**. Incluye once destinos, apariencia configurable, tareas persistentes, previsión del tiempo, Gmail, un [widget de Mercados en la barra lateral](docs/MARKETS.md) y el atajo global Ctrl+H. El prototipo Python/Tkinter pertenece a una etapa anterior; el punto de entrada actual es `HABITAT.vbs`.

## Primera instalación

Se está preparando la distribución mediante WinGet. El instalador, los
manifiestos y el proceso de publicación están descritos en [docs/WINGET.md](docs/WINGET.md).
HABITAT todavía no está disponible en el catálogo público de WinGet.
La solicitud de alta del paquete está abierta en
[microsoft/winget-pkgs#455544](https://github.com/microsoft/winget-pkgs/pull/455544).
Mientras se revisa, puedes descargar el instalador directamente de GitHub Releases.

1. Descarga `HABITAT-0.16.2-x64-setup.exe` y su SHA-256 desde [GitHub Releases](https://github.com/aesgalexis/habitat/releases/latest), comprueba el hash y ejecuta el instalador desde tu sesión normal de Windows. Usa los assets de la publicación; el ZIP **Source code** no contiene la aplicación compilada.
2. Instala en `%LOCALAPPDATA%\Programs\HABITAT`, añade HABITAT al menú Inicio y registra su desinstalador. Incluye .NET y Windows App SDK; no requiere SDK ni permisos de administrador. Compatible con Windows x64, Windows 10 2004 o posterior y Windows 11.
3. Abre **Set up HABITAT…** (aparece la primera vez y permanece en el menú). Detecta Chrome, abre la extensión de [Chrome Web Store](https://chromewebstore.google.com/detail/ennnldolgdbbkgmpjjpkafeajihpimii) y muestra el estado de cada conexión. Aprueba la instalación en Chrome y pulsa **Enable connection**.
4. La extensión 0.9.0 está enviada como Unlisted y pendiente de revisión de Google al preparar esta versión. Si el enlace aún no está disponible, el asistente abre la carpeta incluida y `chrome://extensions` para cargarla descomprimida. Chrome no se instala ni añade extensiones silenciosamente.
5. Para ChatGPT, inicia sesión y abre una conversación en Chrome. Google, YouTube y Translate no requieren cuenta de ChatGPT. Gmail, VS Code y Codex son opcionales; el asistente explica su configuración.

El paquete incluye browser bridge **0.10.3** y el VSIX **HABITAT Terminal 0.2.0**. El instalador todavía no tiene firma de código. Consulta la [guía para otro PC](docs/GETTING_STARTED.md) y los [detalles de distribución](docs/RELEASING.md). Si prefieres trabajar desde el código fuente, clona el repositorio y sigue **Compilar**.

Como alternativa, extrae todo `HABITAT-0.16.2-win-x64.zip` y ejecuta `Install-HABITAT.cmd`. Usa siempre el mismo formato para actualizar: EXE y ZIP mantienen disposiciones distintas y rechazan sobrescribirse entre sí. Ambos conservan los datos de usuario; el ZIP conserva además la publicación anterior para recuperación. La preparación para WinGet se documenta en [WINGET.md](docs/WINGET.md).

HABITAT busca versiones estables nuevas para Windows al arrancar y cada seis horas mientras permanece abierto. Pulsa **Actualizar HABITAT**, al final del sidebar, para descargar el EXE o ZIP correspondiente, verificar su SHA-256, instalarlo y reabrir la app. Puedes cancelar la descarga; HABITAT espera las acciones en curso y conserva borradores, tareas y preferencias. Los fallos se reintentan tras una hora. Las versiones anteriores a 0.15.0 necesitan una actualización manual para incorporar esta función. Consulta [las actualizaciones desde la app](docs/AUTO_UPDATES.md).

## Ejecutar

HABITAT se comporta como una ventana normal. Después de pasar a otra aplicación o al escritorio, haz clic en HABITAT para volver a activarlo. También puedes usar Alt+Tab, la barra de tareas o Ctrl+H si tienes habilitado el atajo global.

1. Abre `HABITAT.vbs`. Inicia la aplicación sin mostrar una consola.
2. En Chrome, selecciona la pestaña y conversación de ChatGPT que quieras usar.
3. Si aún no está instalada, carga `extension` desde `chrome://extensions` con **Modo desarrollador → Cargar descomprimida**.
4. Espera a que HABITAT muestre **Extension connected**.

La extensión se vuelve a emparejar automáticamente cuando HABITAT cambia su clave al reiniciarse. El código manual sigue disponible en el menú de tres líneas de la barra superior, junto a minimizar como mecanismo de recuperación.

Chrome puede quedarse en segundo plano, minimizado o mostrando otra pestaña. La extensión conserva el último chat vinculado al seleccionar una pestaña de otro sitio, y se reconecta al mismo chat aunque esté en segundo plano. Usa un WebSocket local desde su proceso de fondo (Chrome 120 o posterior), no temporizadores dentro de la página oculta. Las comprobaciones de conexión siguen funcionando mientras ChatGPT responde. La pestaña vinculada se excluye temporalmente del descarte automático de Chrome; su valor anterior se restablece al desconectar HABITAT. No se cambian ajustes globales, perfiles ni cookies.

Si Chrome ya había descartado o congelado completamente esa pestaña, HABITAT lo indica y necesita que la abras una vez para reanudarla; no la recarga por su cuenta ni borra un posible borrador. Cerrar Chrome o suspender Windows también interrumpe la conexión. Al seleccionar otro chat de ChatGPT, la vinculación lo sigue entre solicitudes; un mensaje ya encolado o una respuesta en curso permanece en su chat original.

En el editor, Enter envía el mensaje y Shift+Enter inserta un salto de línea. Las respuestas de texto vuelven a HABITAT y permanecen también en la conversación original de ChatGPT.

Los destinos aparecen separados en el lado derecho de la ventana, centrados en altura: **OpenAI**, **Google**, **YouTube**, **Notepad**, **Translate**, **Gmail**, **Google Maps**, **Codex**, **VS Code**, **Terminal** y **Run**. Se seleccionan haciendo clic o con **la rueda del ratón sobre la zona del selector**, incluidos los espacios entre opciones, sin pulsar Ctrl. **Ctrl + rueda del ratón** sigue funcionando desde cualquier zona de la ventana cuando HABITAT tiene el foco. La rueda hacia arriba retrocede de forma circular; hacia abajo avanza. El destino activo se resalta y queda guardado. Cambiarlo conserva el borrador y adapta la indicación y la acción del botón. La indicación de cada modo aparece por encima del editor vacío, separada por 16 píxeles, tanto en el centro de la ventana como en el editor inferior de una conversación. Se oculta al escribir.

La pantalla inicial de todos los destinos tiene siempre el mismo formato: indicación de la acción sobre el editor, editor centrado con cursor y botón para ejecutar/enviar la acción debajo. Solo cambian las etiquetas del destino. No incluye explicaciones de uso ni la sección de resultados o sus acciones hasta ejecutar. En Gmail son **Draft a new email…**, cursor y **Draft email**. **Clear** devuelve esa pantalla inicial y el foco al editor.

El borrador sin ejecutar acompaña al usuario entre modos. Al iniciar una acción, ese texto queda asociado al modo utilizado y deja de aparecer en los demás. Translate conserva su original y resultado al volver; los envíos y lanzadores externos siguen limpiando el texto enviado al completarse correctamente. Un borrador nuevo puede viajar entre los modos libres sin reemplazar el texto propio de otro modo. Vaciar un texto usado permite empezar otro borrador compartido si no hay uno pendiente. Una acción pendiente nunca borra una revisión más reciente, aunque contenga el mismo texto. Cancelar la confirmación de Terminal o rechazar un envío antes de iniciarlo conserva el borrador compartido; un fallo posterior conserva el texto en su modo para reintentar.

**Codex** mantiene una conversación vinculada en HABITAT aunque selecciones otro chat o abras uno nuevo en Codex. Guarda su identificador y los de los chats iniciados desde HABITAT en `%LOCALAPPDATA%\HABITAT\codex-chats.json`, de modo que recupera la misma conversación tras reiniciar. Lee sus respuestas por identificador sin cambiar el chat visible de Codex. Para enviar, vuelve a abrir y verifica el chat vinculado en Codex; protege los borradores existentes y los turnos en ejecución. **Nuevo chat** limpia el vínculo y el editor de HABITAT, conserva la conversación anterior en Codex y devuelve el foco al editor inicial centrado. El siguiente envío crea otro chat; la selección vacía también se conserva tras reiniciar. **Abrir en Codex** abre el chat vinculado y **Recargar chat** actualiza su historial. No se importan conversaciones creadas directamente en Codex ni chats antiguos sin origen registrado. En la primera actualización desde un registro anterior, se vincula una sola vez el chat visible si está registrado como iniciado desde HABITAT; si hay otro chat visible, se recupera la última conversación registrada disponible. El historial sigue separado del de **OpenAI**. Con varias ventanas, activa una vez la que quieras utilizar. Los títulos duplicados requieren renombrar el chat para poder verificar su identidad al enviar.

**Codex** muestra **Última configuración conocida · Modelo: … · Razonamiento: …** debajo del título del chat vinculado. Los valores proceden de los metadatos guardados por Codex y se actualizan para el identificador vinculado, incluidos los cambios de título, independientemente de la navegación en Codex. Los campos ausentes muestran **No disponible**; las etiquetas y los niveles de razonamiento siguen el idioma de la interfaz. La pantalla inicial conserva su disposición.

El botón **Send to Codex** cambia a **Sending to Codex…** durante la entrega y a **Waiting for Codex…** mientras llega la respuesta. HABITAT verifica la selección, el editor, la instrucción completa y el foco antes de pulsar Enter una sola vez. Conserva espacios, Unicode, saltos de línea y Markdown literal. Protege los borradores escritos directamente en Codex y evita enviar mientras su turno está en ejecución. Solo limpia su editor cuando confirma la entrega; si pierde la confirmación, conserva el texto y pide comprobar Codex antes de repetir. Editar durante la preparación cancela ese intento sin borrar una revisión posterior. Editar después de confirmar el envío conserva la espera de la respuesta. **Stop waiting** detiene la espera local; el turno sigue bajo el control de Codex. La lectura utiliza el servidor local incluido en Codex, con operaciones de consulta exclusivamente: no reanuda chats ni inicia turnos adicionales. Se muestran los últimos veinte turnos, con los mensajes del usuario y las respuestas finales de texto. Las herramientas, archivos y solicitudes de aprobación se consultan en Codex.

Las comprobaciones normales usan un escritorio sintético y un servidor JSON-RPC simulado. **--codex-ui-only** verifica la interfaz, los cambios de chat, las respuestas tardías y la separación del historial de OpenAI con datos aislados. La prueba explícita **--codex-conversation-smoke** de la compilación WinUI envía dos mensajes sintéticos a un chat nuevo real y comprueba que HABITAT recupera ambas respuestas y mantiene el contexto. **--codex-desktop-smoke** y **--codex-multiline** conservan la comprobación específica de entrega literal. Estas pruebas reales piden respuestas con marcadores, sin herramientas ni cambios de archivos.

**OpenAI** conserva el envío habitual. **Google** y **YouTube** muestran sus resultados de búsqueda dentro de HABITAT. **Notepad** reutiliza una ventana abierta (también si está minimizada) y crea en ella una pestaña nueva sin nombre. Solo inicia la aplicación si no hay ninguna ventana de Notepad abierta. Inserta el contenido completo, incluidos espacios, Unicode y saltos de línea, sin crear ni guardar archivos. La pestaña se crea con el botón nativo identificado por accesibilidad, sin depender de atajos que cambian con el idioma de Notepad (por ejemplo, Ctrl+N activa negrita en español). En una versión clásica sin pestañas, abre otra ventana para conservar el documento anterior. El editor de HABITAT se limpia solo después de comprobar que Notepad recibió el texto. **Gmail** guarda un borrador directamente en la cuenta conectada al pulsar **Draft email**, sin abrir el navegador. Usa las primeras ocho palabras como asunto, conserva el cuerpo completo y el texto en el editor, sin destinatario y sin enviarlo. **Clear** limpia el editor sin borrar borradores guardados; **Open in browser** abre el último borrador o la carpeta de borradores, sin crear otro. Si solo tienes permiso de lectura, **Authorize Gmail drafts** permite autorizar el guardado una vez desde Google; después pulsa **Draft email** otra vez. **Google Maps** busca lugares o direcciones en Chrome. Si escribes un trayecto, por ejemplo `distancia de Madrid a Barcelona`, `distancia entre Palma de Mallorca y Sóller`, `ruta de Sevilla a Cádiz` o `cómo llegar de Valencia a Alicante`, abre directamente las indicaciones con origen y destino, para ver la ruta, distancia y tiempo en Google Maps. También reconoce `de Madrid a Barcelona`, `desde Madrid hasta Barcelona` y sus equivalentes en inglés (`distance from London to Oxford`, `directions from London to Oxford`). Admite nombres, direcciones y coordenadas; las búsquedas sin un trayecto completo conservan el texto original. La indicación del editor es **Search places or get directions…**. **Terminal** muestra **Run PowerShell command**, con el comando completo y el directorio de trabajo (la carpeta personal de Windows). Solo **Run command** lo ejecuta en una nueva ventana de PowerShell, que permanece abierta para ver la salida. **Cancel** es la opción predeterminada y conserva el texto sin ejecutar nada. Enter o el botón ejecutan la acción y limpian el editor al abrirla correctamente; si falla, el borrador se conserva para reintentar. Los destinos externos no añaden resultados al historial ni requieren conexión con la extensión. Google, YouTube y Translate usan su sección integrada y la extensión.

**Translate** conserva la vista de escritura habitual hasta pulsar Enter o **Translate**. El primer envío traduce a **English** y muestra el resultado y el selector **Translate to**. Solo están disponibles **English**, **Greek** y **Spanish**; cada nueva sesión empieza en inglés. El original permanece en el editor y el icono **Copy translation**, situado debajo de la traducción entregada, copia únicamente el resultado. **Open in browser** abre Google Translate con el original enviado y el idioma seleccionado. **Clear translation** cancela la espera, borra original y resultado y devuelve el editor centrado al inicio; conserva el idioma elegido. Puedes cambiar de modo durante una traducción y recuperarla al volver; el historial de OpenAI se conserva separado. **Cancel** detiene la espera y **Retry** repite el último texto tras un fallo. Cambiar el idioma descarta respuestas anteriores; pulsa de nuevo **Translate** para traducir al idioma elegido.

La traducción utiliza la página real de Google Translate en Chrome mediante la extensión **HABITAT · browser bridge** (versión 0.5.3 o posterior): reutiliza una pestaña abierta del traductor, también si se abrió manualmente o pertenece a otra ventana de Chrome, mantiene activa únicamente la página de traducción mediante emulación de foco, rellena el texto y devuelve el resultado a HABITAT. No selecciona la pestaña, no activa ni restaura la ventana y no cambia su estado minimizado. La emulación se libera antes de devolver el resultado, también al cancelar o fallar. Solo crea una pestaña si no hay ninguna disponible y la reutiliza para las siguientes traducciones, incluso después de perder el identificador de sesión. Los trabajos solapados comparten la misma pestaña. No reemplaza otras páginas del usuario ni envía mensajes a ChatGPT. No se usa una API de traducción, un navegador integrado ni el portapapeles global para transportar el resultado. Chrome debe permanecer abierto y la extensión necesita acceso a `translate.google.com` y el permiso `debugger` para mantener activa esa página sin mostrarla. Solo adjunta la automatización a la pestaña del traductor durante cada trabajo; Chrome puede mostrar su aviso habitual de depuración si abres el navegador. Si su DevTools está abierto, ciérralo antes de traducir. Recarga la extensión desde `chrome://extensions` al actualizar sus archivos. Si Google muestra consentimiento u otro bloqueo, abre su pestaña y complétalo antes de reintentar. Se aplica el máximo de 5.000 caracteres de la página. El escritorio limita la espera a 55 segundos; **Cancel** libera de inmediato la interfaz, incluso si el navegador no responde. **Copy** es la única acción que escribe el resultado en el portapapeles del sistema.

El modo **Text-only replies** está activado por defecto. Añade a cada envío una instrucción visible para que ChatGPT responda con texto simple y evite tablas, imágenes, archivos y otros contenidos enriquecidos, salvo que el propio mensaje los solicite expresamente. Se puede desactivar y su valor queda guardado desde **Preferences…**.

Para acotar la memoria, la vista local de OpenAI conserva hasta 200 mensajes y un presupuesto de 2.000.000 de caracteres; el último mensaje se conserva íntegro. Si retira mensajes antiguos muestra **Earlier messages remain available in ChatGPT.** La conversación original sigue completa en Chrome. Codex conserva en memoria los últimos veinte turnos por chat y hasta 32 chats visitados, protegiendo el seleccionado y cualquier respuesta pendiente; los demás se vuelven a consultar al abrirlos.

El icono de calendario junto a **Enviar a Codex** abre los [mensajes programados](docs/CODEX_SCHEDULING.md). Elige **Nuevo chat de Codex** para programar tu primer mensaje, o un chat iniciado desde HABITAT; después selecciona fecha y hora. Los chats detenidos por límite de uso aparecen primero y ofrecen la hora de recuperación cuando está disponible. Puedes editar y cancelar la cola. También se incluyen los chats iniciados directamente en Codex que estén detenidos por límite: selecciona uno o varios, usa Continúa y elige la hora de recuperación. Cada reanudación se vincula al turno interrumpido y no se envía si ese chat ya ha avanzado. HABITAT debe permanecer abierto y Windows con la sesión iniciada. Las programaciones sobreviven a un reinicio; los envíos dudosos y los retrasados más de cinco minutos quedan para revisión y no se repiten automáticamente.

## Run

**Run**, al final del selector y debajo de **Terminal**, abre programas y comandos de Windows como `calc`, `msconfig`, `control` o `services.msc`. Escribe el comando y pulsa Enter o **Run**. La indicación del editor es **Run a program or a command…**. Admite argumentos, rutas de programa entre comillas y variables como `%USERPROFILE%`; también abre archivos y carpetas existentes. Usa la apertura del shell de Windows desde la carpeta personal, sin añadir una consola. Windows puede solicitar sus permisos habituales si la herramienta los requiere. Tras aceptar el lanzamiento limpia el editor; si falla conserva el comando y muestra **Could not run this command. Check the name or path and try again.** Solo admite una línea por envío. No requiere la extensión del navegador ni añade mensajes al chat.

## VS Code

**VS Code** muestra desde el inicio un selector compacto **Project** y la ruta de la carpeta, por encima del editor, que permanece centrado hasta el primer envío. Por defecto sigue la carpeta del editor activo en la última ventana de VS Code que tuvo el foco. **Choose folder…**, dentro del selector, permite elegir cualquier carpeta local aunque no esté abierta en VS Code. HABITAT recuerda hasta 20 carpetas y conserva la selección manual al cambiar de destino. Elegir una carpeta no ejecuta nada. Al pulsar Enter o **Run in VS Code**, ejecuta directamente sin diálogo de confirmación. Abre el proyecto en VS Code si hace falta y espera hasta 30 segundos a su conexión antes de crear el terminal. Reutiliza las ventanas conectadas y nunca sustituye la carpeta elegida por otra. Una entrega aceptada limpia el borrador; un fallo lo conserva. La sección solo muestra avisos cuando hay un problema.

La extensión se instala una sola vez en VS Code: no hay que añadir código, dependencias ni archivos de configuración a los proyectos. Las carpetas recordadas se guardan en las preferencias de HABITAT. VS Code conserva su comprobación habitual de confianza al abrir un proyecto nuevo.

Puedes ejecutar `npm run dev`, `npm run site:publish` u otros comandos disponibles en el proyecto. Se usa el shell predeterminado de VS Code y cada envío crea otro terminal sin reutilizar ni detener los anteriores. HABITAT muestra únicamente los resultados de la carpeta seleccionada: al cambiar de proyecto se oculta la salida anterior y, al volver, se recupera su historial actualizado. Los comandos de otros proyectos siguen monitorizados en segundo plano. Una carpeta sin envíos conserva el editor centrado. Cada resultado muestra **Starting**, **Running**, **Completed** o **Failed**, duración y código de salida cuando falla. El indicador de progreso permanece activo mientras el comando se ejecuta, sin bloquear nuevos envíos. Un servidor de desarrollo sigue **Running** aunque ya haya mostrado su URL. El resultado incluye las últimas líneas y enlaces HTTP/HTTPS de la salida; **Show output** despliega el texto capturado y **Hide output** lo contrae. La captura se limita a los últimos 65.536 caracteres por comando; si se supera, muestra **Output truncated**. La salida completa permanece en VS Code.

El seguimiento necesita **HABITAT Terminal 0.2.0** y la integración del shell de VS Code. Si no está disponible, no se recibe un código de salida o se pierde la conexión, muestra **Status unavailable**, sin asumir éxito ni repetir el comando. Cerrar el terminal muestra **Closed** cuando no había una finalización confirmada. Si se pierde la confirmación de entrega, revisa el terminal antes de enviar otra vez. Solo admite carpetas locales de confianza; una ventana vacía o remota no se sustituye automáticamente por un proyecto anterior. La salida se transporta únicamente por el puente local y no se envía a un servicio de IA.

La integración necesita la extensión local **HABITAT Terminal**, independiente de la de Chrome. Se empaqueta sin dependencias npm:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-vscode-extension.ps1
code --install-extension artifacts\vscode\habitat-terminal-0.2.0.vsix --force
```

Después de instalarla, recarga las ventanas de VS Code ya abiertas con **Developer: Reload Window**. Las pruebas `node tests/vscode-extension.checks.cjs` y la suite de destinos verifican terminales nuevos, texto exacto, múltiples proyectos, cambios de contexto, confianza, autenticación local y ausencia de reintentos automáticos. Los controles WinUI se prueban con datos sintéticos en `scripts\check-tasks-ui.ps1`, incluida la escritura centrada inicial y la sección compacta sin explicaciones ni botones auxiliares.

## Apariencia y escritura

**Preferencias… → Apariencia → Fondo de pantalla** ofrece tres fondos de color, cinco ilustraciones incluidas (Grafito, Dunas, Marea, Olivo y Órbita) y una biblioteca de imágenes JPG/PNG propias, con vista previa, encuadre y atenuación. **Restaurar original · Híbrido, 15 %** recupera la apariencia transparente predeterminada. Consulta [funcionamiento y almacenamiento de fondos](docs/WALLPAPERS.md).

La interfaz utiliza **inglés**, incluidos los perfiles de versiones bilingües anteriores. Los borradores, tareas, resultados y el idioma de Translate se conservan. Consulta [idioma de la interfaz](docs/LOCALIZATION.md).

**About HABITAT…**, al final del menú, muestra la versión instalada, plataforma, autor, licencia MIT, enlaces al proyecto y soporte, información de privacidad y créditos. **Copy app details** copia la versión y los datos del sistema para informar de incidencias.

Desde **Preferences…** puedes elegir **Day**, **Night** o **Hybrid** y ajustar la opacidad del fondo. El valor por defecto es **Hybrid con un 15 % de opacidad**. Hybrid adapta el contraste al fondo de la ventana. La apariencia y la posición y tamaño de la ventana se conservan entre ejecuciones; las preferencias ya guardadas se mantienen.

Las preferencias se organizan mediante una barra lateral con **Appearance**, **Location**, **ChatGPT** y **Shortcuts**. Cada categoría conserva los cambios al pasar a otra; se guardan al cerrar **Preferences**. El atajo global se aplica al cambiar su interruptor, como antes.

Los destinos web, resultados de búsquedas, Gmail, el inicio de sesión de Google y los enlaces del terminal de VS Code se abren siempre en Google Chrome, donde está instalada la extensión. No hay selector de navegador ni dependencia del navegador predeterminado de Windows. Los protocolos de aplicaciones conservan su manejador de Windows. Las preferencias antiguas de navegador se ignoran sin perder los demás ajustes.

ChatGPT, Google, YouTube y Translate dentro de HABITAT necesitan la extensión **HABITAT · browser bridge 0.10.3** en Chrome y su consentimiento **Enable connection**. La conexión se autentica con la clave local y funciona también con paquetes anteriores, sin filtros asociados a una preferencia de navegador.

El editor está centrado cuando el destino seleccionado no tiene contenido. Al empezar una conversación, pasa a la parte inferior y deja espacio al historial de texto. El historial de OpenAI solo se muestra en su destino: al cambiar se oculta, incluidas las respuestas que lleguen mientras se usa otro destino, y se recupera completo al volver. Pulsar en el fondo de la ventana devuelve el foco al editor.

Las áreas de lectura con scroll comparten un desvanecido suave de 20 píxeles en los bordes con contenido oculto. Deja ver el fondo real de la ventana y desaparece al llegar al inicio o al final. Las cabeceras, las barras de scroll, la selección de texto y los controles nativos conservan su comportamiento.

## Google Search

El destino **Google** muestra dentro de HABITAT una lista de resultados web con título pulsable, dirección y descripción, en el orden de la página consultada. **Previous page** y **Next page** recorren las páginas disponibles. La consulta se conserva para editarla y los resultados permanecen al cambiar de destino. Los enlaces se abren en Chrome al pulsarlos; **Open in browser** abre la búsqueda actual. **Open in browser** y **Clear search** aparecen juntos en la cabecera, arriba a la derecha, con **Clear search** en el extremo derecho. **Cancel** detiene la espera y **Retry** repite la consulta o página que falló. Los estados de error, de carga y de cero resultados aparecen en inglés; los títulos y fragmentos conservan el idioma original de Google.

Vaciar la consulta reinicia Google: borra los resultados, cancela la búsqueda pendiente, vuelve al editor centrado y cierra la pestaña de búsqueda de HABITAT. **Clear search**, arriba a la derecha del listado, realiza la misma acción y vacía también el editor. Cambiar de destino conserva la búsqueda; vaciar otro destino no la reinicia. Se cierra únicamente la pestaña propia de búsqueda, también si muestra consentimiento o verificación de Google; si el usuario la ha navegado a otra web, se conserva. Las pestañas abiertas al pulsar resultados y las otras ventanas de Chrome permanecen abiertas. El cierre pendiente se entrega al reconectar la extensión y se confirma antes de iniciar otra búsqueda.

Requiere Chrome abierto y la extensión **HABITAT · browser bridge 0.6.1** o posterior, con acceso a `www.google.com`. Recarga la extensión en `chrome://extensions` después de actualizar sus archivos y acepta el permiso de búsqueda si Chrome lo solicita. La extensión crea una pestaña propia e inactiva para la vista web de Google y la reutiliza; no sustituye búsquedas personales ni activa ventanas o pestañas. No usa una API de búsqueda ni WebView, no envía consultas a ChatGPT y no guarda consultas o resultados en disco. La sección de Google es independiente de YouTube; Google Maps conserva su funcionamiento. No muestra mapas, anuncios ni otros módulos especiales.

Si Google requiere consentimiento o verificación, completa el paso en su pestaña de Chrome y pulsa **Retry**. Un cambio incompatible de la página muestra un error de lectura. Las comprobaciones habituales usan datos sintéticos; `node tests/extension-search.checks.cjs` comprueba el lector y la pestaña propia y `--search-browser-smoke` permite verificar la página real con una consulta de prueba, con HABITAT cerrado.
## YouTube

El destino **YouTube** muestra vídeos dentro de HABITAT como una lista con miniaturas, títulos pulsables y enlaces, con canal, duración, visualizaciones, antigüedad y descripción cuando YouTube los ofrece. Si el nombre o identificador de un canal coincide con la consulta, ignorando mayúsculas y espacios sobrantes, aparece antes de los vídeos en la primera página con la etiqueta **Channel** y su enlace; el identificador puede escribirse con o sin `@`. **Previous page** y **Next page** recorren bloques de 20 vídeos en el orden de la página, cargando su continuación cuando hace falta; los canales no consumen posiciones de esa paginación. El vídeo se abre en el navegador al pulsar su miniatura o título; el título de un canal abre su página. No se reproduce contenido dentro de HABITAT. **Open in browser** abre la consulta completa en YouTube y aparece junto a **Clear search** en la esquina superior derecha, con la misma distribución que Google.

Las miniaturas son JPEG de 320 × 180, cargados directamente desde el CDN de YouTube cuando entran en pantalla y mostrados en un espacio de hasta 160 × 90. Se usan controles nativos, sin paquetes adicionales ni imágenes dentro del puente de la extensión. Al limpiar, cambiar de página o salir de YouTube se liberan las referencias a las imágenes de esa vista; si una imagen falla, su espacio mantiene un icono y el vídeo sigue disponible.

La consulta, los resultados y la pestaña son independientes de Google. Las consultas consecutivas actualizan la misma pestaña de búsqueda en la misma ventana. Al vaciar el editor, YouTube espera 750 ms antes de reiniciar para permitir sustituir la consulta sin cerrar la pestaña entre ambas; si empiezas a escribir otra, se cancela ese cierre. **Clear search**, arriba a la derecha, reinicia inmediatamente: cancela la búsqueda, limpia la lista, centra el editor y cierra la pestaña propia de resultados. Conserva las pestañas personales y las que abras para ver vídeos. **Cancel** y **Retry** permiten detener o recuperar una búsqueda; cambiar de destino conserva su estado.

Requiere Chrome abierto y **HABITAT · browser bridge 0.7.0** o posterior, con acceso a `www.youtube.com`. Recarga la extensión desde `chrome://extensions` al actualizarla. La búsqueda usa una pestaña propia en segundo plano y emulación de foco de página mediante el permiso `debugger` existente; no selecciona pestañas, restaura ventanas ni cambia el foco de Windows. El sitio puede mostrar consentimiento o verificación: complétalos en Chrome y pulsa **Retry**. Si su DevTools está abierto, ciérralo para permitir la lectura en segundo plano. No se usa la API de YouTube, un reproductor integrado ni ChatGPT para buscar. No se guardan consultas ni resultados en disco.

`node tests/extension-youtube.checks.cjs` verifica el lector, la continuación y el cierre con páginas sintéticas. La prueba opcional `--youtube-browser-smoke`, con HABITAT cerrado, comprueba la página real mediante una consulta de prueba, incluida la segunda página y el reinicio, y cierra la pestaña utilizada al terminar.
## Tiempo y ubicación

El tiempo aparece arriba del todo en la barra lateral izquierda, por encima de Gmail y Tasks, sin marco. Con la barra recogida muestra el icono y la temperatura en °C debajo; al desplegarla añade la ubicación y la previsión de lluvia para las próximas seis horas. Conserva su posición al cambiar de estado y la lista de tareas se abre por debajo de los tres widgets. El contenido principal mantiene una franja superior libre de 100 px, equivalente al espacio reservado por el antiguo widget independiente. **Use current location (GPS / Windows)** está activado por defecto en Preferences: usa el servicio de ubicación de Windows, con GPS cuando está disponible. Solicita el permiso desde HABITAT y actualiza la posición junto al tiempo cada 15 minutos, sin seguimiento continuo. Las coordenadas se redondean a tres decimales y se envían a [Open-Meteo](https://open-meteo.com/en/docs) para obtener el pronóstico; no se guardan en disco ni se envían al chat.

Si Windows no obtiene una posición o deniega el acceso, usa la ciudad guardada (inicialmente `Palma, ES`) e indica **saved city**. El widget no muestra tooltips al pasar el ratón. En **Preferences…** se puede desactivar la ubicación automática, cambiar **Fallback city / Weather location**, o abrir **Windows location settings**. En Windows: **Configuración → Privacidad y seguridad → Ubicación → Servicios de ubicación** y el permiso para aplicaciones de escritorio. El porcentaje mostrado es el máximo de las probabilidades horarias de precipitación de las próximas seis horas. Si falla la conexión, lo indica y reintenta automáticamente.

El icono **Open in new window** aparece únicamente a la izquierda de la barra del destino seleccionado. Ejecuta su acción con el texto actual del editor, aunque aún no se haya pulsado el botón principal, y muestra la ventana externa: Gmail abre un borrador sin enviarlo; Google y YouTube abren la búsqueda; Maps conserva las rutas; Translate abre Google Translate con el idioma elegido y conserva el original. Notepad, Codex, Terminal y Run usan sus lanzadores habituales. Terminal mantiene la confirmación previa; VS Code ejecuta directamente en el proyecto seleccionado. Con el editor vacío abre una página inicial, un documento o borrador nuevo, una shell vacía o el diálogo Run. VS Code abre el proyecto seleccionado; sin proyecto abre la aplicación. Las etiquetas de ayuda describen la acción concreta en inglés. Ambos botones impiden acciones duplicadas mientras se entrega y un fallo conserva el borrador; una revisión posterior nunca se borra por una entrega anterior.

**Send and open ChatGPT** necesita la extensión de navegador **0.7.1** o posterior: selecciona la conversación vinculada y restaura su ventana antes de enviar una vez. El botón principal conserva el envío en segundo plano. Sin texto, el icono solo abre esa conversación; sin conexión al navegador abre la página inicial de ChatGPT. **Run command and open VS Code** necesita **HABITAT Terminal 0.1.1** o posterior para mostrar la ventana exacta después de entregar el comando. Si falla la apertura tras una entrega aceptada, lo indica y no repite el comando. Recarga las extensiones después de actualizarlas.

## Destinos personalizados

El estado de la ventana se actualiza al iniciar y cambiar de destino: un destino en reposo muestra **Ready**. La conexión y los avisos del puente de ChatGPT se muestran en OpenAI; sus respuestas en segundo plano se conservan sin reemplazar el indicador de otro destino.

En **Preferences… → Destinations** puedes añadir, quitar y ordenar los destinos de la barra derecha. **Move up / Move down** cambia el orden y **On startup** permite abrir un destino concreto o recordar el último utilizado con **Last used destination**. La rueda recorre únicamente los destinos añadidos. **Remove** conserva la configuración y el contenido de la sesión; puedes recuperar el destino desde **Available destinations → Add destination**. Si quitas todos, el botón central **Add destination** abre directamente esta categoría, sin borrar el borrador.

**Add Windows application…** consulta las aplicaciones instaladas de Windows, incluidas las de Microsoft Store. También puedes usar **Browse…** para elegir un ejecutable `.exe` o un acceso directo `.lnk`, y darle un nombre propio. La lista se consulta al abrir este selector; no se inicia ninguna aplicación al añadirla. Los cambios se aplican al cerrar Preferences y se conservan al reiniciar. Los destinos nuevos del catálogo no se incorporan automáticamente a tu selección.

Por defecto, **Open [application]** abre la aplicación, incluso con el editor vacío, y conserva el borrador. Por ejemplo, Paint se puede añadir como lanzador; este modo no inserta texto ni dibuja automáticamente. Para ejecutables que admitan texto por línea de comandos, **Pass editor text as one argument** pasa el contenido completo como un único argumento literal y limpia el borrador solo si Windows acepta el lanzamiento. Esto confirma la apertura, no que la aplicación haya procesado el texto. Los accesos directos y aplicaciones de Store se abren sin argumentos. Un fallo conserva el borrador para reintentar o recuperar el destino desde Preferences.

La configuración utiliza identificadores estables y migra los antiguos valores numéricos sin cambiar la selección existente. Las pruebas de destinos usan ejecutables sintéticos y lanzadores interceptados, sin abrir aplicaciones reales. `dotnet run --project tests\Habitat.Destination.Checks -- --windows-apps-smoke` comprueba explícitamente la enumeración de aplicaciones instaladas sin abrirlas. **--preferences-ui-only** comprueba la gestión, persistencia, creación de destinos Windows y la disposición inicial común con almacenamiento aislado dentro de `artifacts`.

### Personalizar la barra lateral

**Preferencias → Barra lateral** permite añadir, quitar y ordenar Gmail, Tareas y Mercados. Tiempo permanece fijo arriba. También puedes hacer clic derecho en la barra y elegir **Personalizar barra lateral…**. Quitar un elemento conserva sus datos y configuración. Mercados está desactivado en instalaciones nuevas; los perfiles anteriores conservan sus elementos. Los cambios se aplican al cerrar Preferencias. Véase [comportamiento y pruebas](docs/SIDEBAR.md).

## Tareas

Los widgets forman una barra lateral izquierda: **Weather** arriba, **Gmail** en medio y **Tasks** debajo, los tres con el mismo ancho. La barra comienza recogida a una franja de iconos. Un clic en su fondo libre muestra las vistas previas; al empezar una conversación, vuelve a recogerse y cede espacio al chat. Al volver a un destino sin conversación permanece recogida por defecto. En ventanas estrechas también usa ese modo compacto. Un clic en el fondo libre de la barra permite recogerla o desplegarla manualmente, también durante una conversación. Los iconos y controles de los widgets conservan sus propias acciones; no hay un botón adicional. La franja recogida mide 40 px; sus iconos están centrados y conservan exactamente las mismas posiciones horizontal y vertical al expandir. El margen exterior izquierdo es de 12 px en ambos estados. Los widgets desplegados conservan su diseño compacto, con icono a la izquierda y texto a la derecha dentro de márgenes uniformes; el borde izquierdo de la barra conserva su posición al cambiar de estado. Al pulsarlo se despliegan el campo de creación y la lista directamente sobre el fondo de HABITAT, siempre por debajo de los elementos activados, también al abrir desde el icono con la barra recogida o al cambiar su estado con la lista abierta. El panel reserva espacio para no solaparse con el chat; en ventanas muy estrechas lo oculta temporalmente y lo restaura al cerrar la lista. Puedes añadir tareas con **+** o Enter, marcarlas como terminadas y recuperarlas desmarcando su casilla en la sección plegable **Completed**. Con la lista cerrada, la vista previa rota cada 8 segundos entre las tareas pendientes; se pausa al pasar el ratón, enfocar el widget o abrir la lista. Al pulsar de nuevo el widget se recoge la lista. También se abre desde **☰ → Tasks…**.

La lista se guarda automáticamente en `%LOCALAPPDATA%\HABITAT\tasks.json` y se conserva entre reinicios y actualizaciones. Cada cambio conserva copias verificadas en `tasks.json.history`. Un índice con checksum acelera las aperturas sucesivas; si cambia el historial o se daña el índice, se vuelven a verificar las revisiones originales. No se elimina ninguna revisión. La lectura y el guardado se serializan fuera del hilo de interfaz. Si falta el archivo principal, está dañado o pierde tareas presentes en el historial, se recuperan las tareas y sus estados de las copias y se muestra un aviso; el archivo anterior se preserva. Una sesión antigua no puede sobrescribir cambios más recientes ni borrar tareas: se ofrece **Reload tasks** y se conserva el borrador. Los errores de lectura o guardado bloquean nuevas modificaciones hasta recargar. HABITAT impide usar una lista temporal al arrancar dentro del sandbox de Codex: abre la aplicación desde el acceso directo de Windows. Las actualizaciones se ejecutan fuera de ese sandbox. El tiempo permanece por encima de Gmail en la barra lateral, visible también con la lista de tareas abierta.

Las comprobaciones de almacenamiento y distribución se ejecutan con `dotnet run --project tests\Habitat.Tasks.Checks`.

`powershell -ExecutionPolicy Bypass -File scripts\check-tasks-ui.ps1` comprueba con controles WinUI reales el panel integrado en el fondo, el foco del campo de creación, el despliegue de tareas terminadas en tema claro y oscuro y la reapertura. Usa tareas sintéticas y un archivo aislado dentro de `artifacts`, abre y cierra su propia ventana y nunca lee ni modifica las tareas del usuario ni conecta el correo o el chat. También comprueba el guardado de ambas listas, la conservación del borrador ante conflictos, el inicio con la barra lateral recogida y que el historial de OpenAI y sus respuestas tardías solo aparezcan en su propio destino. Esta compilación de prueba se genera aparte del ejecutable habitual.

## Gmail

El widget superior de la barra lateral muestra el contador de no leídos y tres líneas con remitente, asunto y fragmento del correo. Rota cada **8 segundos** entre los mensajes cargados; se pausa al pasar el ratón, enfocar el mensaje con el teclado o abrir el lector. Durante una conversación o en ventanas estrechas se reduce al icono; el lector se abre hacia la derecha de la barra.

- **Icono y contador:** abren el lector de HABITAT, también disponible en **☰ → Gmail…**. Permite leer texto sin marcar mensajes como leídos, elegir **Inbox unread** o **All unread** y cargar más páginas.
- **Remitente, asunto y fragmento:** abren Gmail en el navegador del sistema y en la cuenta conectada, buscando ese correo por su identificador original. Si no está disponible, se usa el enlace a la conversación.

Las dos zonas tienen su propio marco al pasar el ratón, sin tooltips. La lista se actualiza cada minuto y la rotación no añade consultas a Google. Cada página carga hasta 25 mensajes. El contador de **All unread** es una estimación y aparece con `≈`.

La conexión requiere un cliente OAuth de Google de tipo **Desktop app** con Gmail API habilitada. El uso estándar es gratuito dentro de las cuotas de Google; no necesitas alojar un servidor ni contratar intermediarios. Mantén el proyecto sin facturación vinculada.

HABITAT recupera la autorización del Administrador de credenciales de Windows al abrirse y renueva el acceso automáticamente. Reimportar el mismo cliente conserva la sesión. En modo OAuth **Testing**, Google hace caducar los permisos de renovación a los siete días. **Disconnect** elimina el permiso de renovación local y conserva el cliente para reconectar. Windows protege el cliente y el refresh token en la entrada `HABITAT/Gmail/DesktopAccount/v1`, fuera del repositorio. Las instalaciones anteriores migran automáticamente su archivo cifrado de cuenta; los access tokens temporales se mantienen en memoria.

HABITAT no envía, elimina ni archiva correos, ni comparte su contenido con ChatGPT. Leer dentro de HABITAT no marca mensajes como leídos; abrirlos en Gmail sigue el comportamiento normal de Gmail. La vista de HABITAT usa texto, sin imágenes remotas ni adjuntos.

Las instrucciones completas están en [docs/GMAIL_SETUP.md](docs/GMAIL_SETUP.md). Las comprobaciones sin cuenta real se ejecutan con `dotnet run --project tests\Habitat.Gmail.Checks`.

## Atajo global

**Ctrl+H** abre o enfoca HABITAT y coloca el texto seleccionado en el editor, sin enviarlo. **Preferences… → Global shortcut (Ctrl+H)** permite activar o desactivar el residente y su inicio con Windows. Los borradores existentes requieren confirmación antes de sustituirse. Si la selección o el respaldo del portapapeles no se pueden verificar con seguridad, solo enfoca la aplicación. Consulta [implementación, límites y pruebas](docs/GLOBAL_SHORTCUT.md).

## Compilar

Requisitos de desarrollo:

- Windows 10, versión 2004 o posterior.
- SDK de .NET 10.

Las dependencias del proyecto se restauran desde NuGet durante la compilación.

Genera la versión autocontenida x64 con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1 -Install
```

Ejecuta `-Install` desde tu sesión normal de Windows. La compilación se prepara en un directorio nuevo de `artifacts\staged`, verifica el hash y tamaño de todos los archivos y la instala en `artifacts\releases`. `HABITAT.vbs` resuelve la versión activa mediante `artifacts\current-release.txt`; la instalación anterior en `artifacts\win-x64` sigue siendo compatible. La versión anterior se conserva y se restaura automáticamente si falla el arranque. La aplicación espera a terminar la respuesta de ChatGPT antes de cerrarse, consultando el estado del puente en lugar del título de la ventana.

Sin `-Install`, el script solo prepara y verifica la compilación; imprime la orden para activarla después. No sobrescribe los binarios que se estén usando. Es una aplicación WinUI 3 sin paquete, sin Modo de desarrollador ni consola. `Habitat.sln` contiene la aplicación, la biblioteca compartida, las comprobaciones y la utilidad de Gmail.

Las comprobaciones del pronóstico, sin red ni dependencias de pruebas adicionales, se ejecutan con `dotnet run --project tests\Habitat.Weather.Checks`. Para comprobar la conexión real del servicio de tiempo para Palma: `dotnet run --project tests\Habitat.Weather.Checks -- --live`.

## Comprobaciones

`dotnet run --project tests\Habitat.Destination.Checks --property:BaseOutputPath=C:/proyectos/habitat/artifacts/destination-checks/` verifica el recorrido circular, las URLs de búsqueda, borradores y rutas de Google Maps (origen y destino, variantes en español e inglés, Unicode y búsquedas normales), preferencias, Notepad y PowerShell. Translate comprueba los tres idiomas, el inglés predeterminado, el texto exacto, IDs y conexión del navegador, cancelación y consentimiento pendiente. `node tests/extension-translation.checks.cjs` verifica con un DOM sintético el pegado en la página, lectura del resultado, reutilización de la pestaña, ausencia de cambios de foco y rechazo de idiomas ajenos. `scripts\check-tasks-ui.ps1` comprueba la vista inicial y el selector oculto hasta Enter, controles nativos sin WebView, copia aislada, cambios de modo, reintentos y descarte de respuestas obsoletas. Las pruebas habituales no contactan con Google ni usan textos del usuario. La comprobación opcional `--notepad-smoke`, ejecutada desde la sesión normal de Windows, crea dos pestañas con texto sintético en el Notepad real y verifica la inserción completa antes de comunicar éxito. La comprobación opcional `--translation-browser-smoke` necesita HABITAT cerrado y la extensión actualizada en Chrome: levanta un puente de prueba en el puerto 8765 y traduce frases sintéticas por la página real a los tres idiomas. Nunca crea mensajes de ChatGPT ni lee las tareas del usuario.

Con Node.js y el SDK de .NET instalados:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

El comando común ejecuta las comprobaciones de extensión, puente, núcleo, destinos, Gmail, tareas, tiempo y apariencia, comprueba actualizaciones con fallos simulados y compila toda la solución en Release. Sus salidas y datos de tareas quedan bajo `artifacts\verification`. `-NoRestore` reutiliza las dependencias ya restauradas; `-SkipBuild` omite la compilación final. También se ejecuta desde el hook de subida y GitHub Actions. Las comprobaciones de controles WinUI se ejecutan por separado con `scripts\check-tasks-ui.ps1`; usan credenciales en memoria, red y lanzadores bloqueados y rutas explícitas de prueba.

La verificación incluye también el residente de Ctrl+H y `tests\package.checks.ps1`: comprueba versiones y sintaxis PowerShell, instalación en una ruta con espacios, actualización con archivos bloqueados, conservación de versiones anteriores y rechazo de corrupción o rutas superpuestas. La instalación de prueba utiliza `-NoLaunch -NoShortcuts` bajo `artifacts`, sin abrir la aplicación ni registrar inicio automático.

Las comprobaciones del transporte y Gmail usan páginas, respuestas y cuentas sintéticas, con puertos locales aleatorios cuando hace falta. No abren tu conversación ni consultan tu correo real. Las de Gmail incluyen cifrado, reapertura de sesión, renovación automática y construcción de enlaces al correo. El endpoint local `/status`, con la clave de vinculación, ofrece diagnóstico de solo lectura: consultarlo no crea un heartbeat ni consume mensajes.

## Seguridad y límites

- El servidor escucha exclusivamente en `127.0.0.1:8765` y exige una clave aleatoria nueva en cada ejecución.
- No publiques el código de vinculación.
- Los mensajes nuevos van al chat activo de Chrome; selecciona la conversación deseada antes de enviar.
- Los selectores de la extensión dependen de la interfaz web de ChatGPT y pueden requerir mantenimiento cuando ésta cambie.
- Widgets, archivos, imágenes y otras interfaces complejas no se reproducen en HABITAT; consúltalos en Chrome.
- No repitas un envío fallido sin revisar antes Chrome: el mensaje podría haber llegado aunque la confirmación no haya vuelto.

## Checkpoints automáticos en GitHub

Los hooks versionados en `.githooks` trabajan sobre la rama `codex/autosave`. Antes de subir, ejecutan `scripts/verify.ps1` con todas las suites, compilación Release y revisión del diff. El hook `post-commit` envía automáticamente el commit a `origin/codex/autosave`. `main` contiene el estado integrado; las publicaciones se fijan mediante etiquetas `vMAJOR.MINOR.PATCH` y assets de GitHub Releases.

Activa los hooks en un clon nuevo con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```

## Documentación y estructura

- [Versiones y distribución](docs/RELEASING.md): SemVer, instalación, actualización y publicación automatizada.
- [Changelog](CHANGELOG.md): contenido de cada publicación.
- [Preparación para WinGet](docs/WINGET.md): instalador EXE, manifiestos y requisitos pendientes de publicación pública.
- [Configuración de Gmail](docs/GMAIL_SETUP.md): OAuth, sesión persistente, coste, privacidad y utilidad de terminal.
- [Estado y dirección del producto](docs/PRODUCT_STATE_AND_VISION.md): funcionalidad implementada, experiencia de uso e ideas pendientes.
- [Arquitectura interna](docs/ARCHITECTURE.md): composición, estados, cierre, almacenamiento y actualizaciones.
- [Auditoría del 18 de octubre de 2026](docs/ARCHITECTURE_AUDIT_2026-10-18.md): problemas corregidos, verificación y deuda arquitectónica priorizada.
- [Contrato del puente](docs/BRIDGE_PROTOCOL.md): mensajes, compatibilidad y transiciones.
- `src/Habitat.App`: ventana, controles WinUI, composición e integración específica con Windows.
- `src/Habitat.Core`: modelos, coordinadores de funcionalidades, servicios y almacenamiento compartidos con las pruebas.
- `src/Habitat.Activation` y `src/Habitat.Hotkey`: activación local y residente de Ctrl+H.
- `extension`: puente con la sesión de ChatGPT en Chrome.
- `tests`: comprobaciones automatizadas sin depender de conversaciones o buzones reales.
- `tools/Habitat.Gmail.Setup`: importación OAuth y comprobación de la cuenta desde terminal.
- `scripts`: compilación, instalación de actualizaciones y checkpoints.
