# HABITAT — WinUI 3

Aplicación de escritorio nativa para Windows que permite escribir en el chat activo de ChatGPT en Chrome y recibir sus respuestas dentro de HABITAT. No crea chats separados, no rota conversaciones ni cambia sus nombres. Usa la sesión real de Chrome, sin consumir la API de OpenAI. Una respuesta en curso termina en su chat original antes de seguir el chat activo.

La aplicación actual está desarrollada en **C# y WinUI 3 sobre .NET 10**. Incluye una superficie de escritura con apariencia configurable, previsión del tiempo y un widget de Gmail. El prototipo Python/Tkinter pertenece a una etapa anterior; el punto de entrada actual es `HABITAT.vbs`.

## Primera instalación

1. Clona el repositorio en Windows y compila siguiendo la sección **Compilar**.
2. Carga la carpeta `extension` en Chrome, versión 120 o posterior.
3. Abre `HABITAT.vbs` y una conversación de ChatGPT en Chrome con tu sesión iniciada.
4. Configura Gmail desde **Setup…** si quieres usar el widget de correo. Es opcional y no requiere un servicio de pago.

## Ejecutar

1. Abre `HABITAT.vbs`. Inicia la aplicación sin mostrar una consola.
2. En Chrome, selecciona la pestaña y conversación de ChatGPT que quieras usar.
3. Si aún no está instalada, carga `extension` desde `chrome://extensions` con **Modo desarrollador → Cargar descomprimida**.
4. Espera a que HABITAT muestre **Extensión conectada**.

La extensión se vuelve a emparejar automáticamente cuando HABITAT cambia su clave al reiniciarse. El código manual sigue disponible en el menú de tres líneas de la barra superior, junto a minimizar como mecanismo de recuperación.

Chrome puede quedarse en segundo plano, minimizado o mostrando otra pestaña. La extensión conserva el último chat vinculado al seleccionar una pestaña de otro sitio, y se reconecta al mismo chat aunque esté en segundo plano. Usa un WebSocket local desde su proceso de fondo (Chrome 120 o posterior), no temporizadores dentro de la página oculta. Las comprobaciones de conexión siguen funcionando mientras ChatGPT responde. La pestaña vinculada se excluye temporalmente del descarte automático de Chrome; su valor anterior se restablece al desconectar HABITAT. No se cambian ajustes globales, perfiles ni cookies.

Si Chrome ya había descartado o congelado completamente esa pestaña, HABITAT lo indica y necesita que la abras una vez para reanudarla; no la recarga por su cuenta ni borra un posible borrador. Cerrar Chrome o suspender Windows también interrumpe la conexión. Al seleccionar otro chat de ChatGPT, la vinculación lo sigue entre solicitudes; un mensaje ya encolado o una respuesta en curso permanece en su chat original.

En el editor, Enter envía el mensaje y Shift+Enter inserta un salto de línea. Las respuestas de texto vuelven a HABITAT y permanecen también en la conversación original de ChatGPT.

Los destinos aparecen separados en el lado derecho de la ventana, centrados en altura: **OpenAI**, **Google**, **YouTube**, **Notepad**, **Translate**, **Gmail**, **Terminal**, **Google Maps** y **Codex**. Se seleccionan haciendo clic o con **Ctrl + rueda del ratón** cuando HABITAT tiene el foco. La rueda hacia arriba retrocede de forma circular; hacia abajo avanza. El destino activo se resalta y queda guardado. Cambiarlo conserva el borrador y adapta la indicación y la acción del botón. La indicación de cada modo aparece por encima del editor vacío, separada por 16 píxeles, tanto en el centro de la ventana como en el editor inferior de una conversación. Se oculta al escribir.

**Codex** copia la instrucción completa al portapapeles y abre la app de Codex instalada en Windows. El usuario pega el contenido y lo envía en el chat activo. La acción del botón es **Copy to Codex**; si la app no puede abrirse, HABITAT conserva el borrador.

**OpenAI** conserva el envío habitual. **Google** y **YouTube** abren una búsqueda en el navegador predeterminado. **Translate** abre Google Translate con el contenido completo, detección automática del idioma de origen y el idioma de Windows como destino inicial; se puede cambiar en la página. **Notepad** reutiliza una ventana abierta (también si está minimizada) y crea en ella una pestaña nueva sin nombre. Solo inicia la aplicación si no hay ninguna ventana de Notepad abierta. Inserta el contenido completo, incluidos espacios, Unicode y saltos de línea, sin crear ni guardar archivos. En una versión clásica sin pestañas, si ya está abierta, conserva el borrador en HABITAT. El editor de HABITAT se limpia solo después de comprobar que Notepad recibió el texto. **Gmail** abre un correo nuevo con el texto como cuerpo, sin destinatario y sin enviarlo. **Google Maps** busca lugares o direcciones en el navegador predeterminado. **Terminal** muestra **Run PowerShell command**, con el comando completo y el directorio de trabajo (la carpeta personal de Windows). Solo **Run command** lo ejecuta en una nueva ventana de PowerShell, que permanece abierta para ver la salida. **Cancel** es la opción predeterminada y conserva el texto sin ejecutar nada. Enter o el botón ejecutan la acción y limpian el editor al abrirla correctamente; si falla, el borrador se conserva para reintentar. Estos destinos externos no añaden resultados al historial ni requieren conexión con la extensión.

El modo **Text-only replies** está activado por defecto. Añade a cada envío una instrucción visible para que ChatGPT responda con texto simple y evite tablas, imágenes, archivos y otros contenidos enriquecidos, salvo que el propio mensaje los solicite expresamente. Se puede desactivar y su valor queda guardado desde **Preferences…**.

## Apariencia y escritura

Desde **Preferences…** puedes elegir **Day**, **Night** o **Hybrid** y ajustar la opacidad del fondo. El valor por defecto es **Hybrid con un 15 % de opacidad**. Hybrid adapta el contraste al fondo de la ventana. La apariencia y la posición y tamaño de la ventana se conservan entre ejecuciones; las preferencias ya guardadas se mantienen.

El editor está centrado cuando no hay mensajes. Al empezar una conversación, pasa a la parte inferior y deja espacio al historial de texto. Pulsar en el fondo de la ventana devuelve el foco al editor.

## Tiempo y ubicación

El tiempo aparece en la esquina superior derecha, sin marco, con temperatura en °C y previsión de lluvia para las próximas seis horas. **Use current location (GPS / Windows)** está activado por defecto en Preferences: usa el servicio de ubicación de Windows, con GPS cuando está disponible. Solicita el permiso desde HABITAT y actualiza la posición junto al tiempo cada 15 minutos, sin seguimiento continuo. Las coordenadas se redondean a tres decimales y se envían a [Open-Meteo](https://open-meteo.com/en/docs) para obtener el pronóstico; no se guardan en disco ni se envían al chat.

Si Windows no obtiene una posición o deniega el acceso, usa la ciudad guardada (inicialmente `Palma, ES`) e indica **saved city**. El widget no muestra tooltips al pasar el ratón. En **Preferences…** se puede desactivar la ubicación automática, cambiar **Fallback city / Weather location**, o abrir **Windows location settings**. En Windows: **Configuración → Privacidad y seguridad → Ubicación → Servicios de ubicación** y el permiso para aplicaciones de escritorio. El porcentaje mostrado es el máximo de las probabilidades horarias de precipitación de las próximas seis horas. Si falla la conexión, lo indica y reintenta automáticamente.

## Tareas

Los widgets forman una barra lateral izquierda: **Gmail** arriba y **Tasks** debajo, ambos con el mismo ancho. Sin conversación muestran sus vistas previas; al aparecer un mensaje del usuario o del asistente, la barra se recoge a una franja de iconos y cede espacio al chat. En ventanas estrechas también usa ese modo compacto. Un clic en el fondo libre de la barra permite recogerla o desplegarla manualmente, también durante una conversación. Los iconos y controles de los widgets conservan sus propias acciones; no hay un botón adicional. La franja recogida mide 40 px; sus iconos están centrados y conservan exactamente las mismas posiciones horizontal y vertical al expandir. El margen exterior izquierdo es de 12 px en ambos estados. Los widgets desplegados conservan su diseño compacto, con icono a la izquierda y texto a la derecha dentro de márgenes uniformes; el borde izquierdo de la barra conserva su posición al cambiar de estado. Al pulsarlo se despliegan el campo de creación y la lista directamente sobre el fondo de HABITAT, siempre por debajo de ambos widgets, también al abrir desde el icono con la barra recogida o al cambiar su estado con la lista abierta. El panel reserva espacio para no solaparse con el chat; en ventanas muy estrechas lo oculta temporalmente y lo restaura al cerrar la lista. Puedes añadir tareas con **+** o Enter, marcarlas como terminadas y recuperarlas desmarcando su casilla en la sección plegable **Completed**. Con la lista cerrada, la vista previa rota cada 8 segundos entre las tareas pendientes; se pausa al pasar el ratón, enfocar el widget o abrir la lista. Al pulsar de nuevo el widget se recoge la lista. También se abre desde **☰ → Tasks…**.

La lista se guarda automáticamente en `%LOCALAPPDATA%\HABITAT\tasks.json` y se conserva entre reinicios y actualizaciones. Cada cambio conserva copias verificadas en `tasks.json.history`. Un índice con checksum acelera las aperturas sucesivas; si cambia el historial o se daña el índice, se vuelven a verificar las revisiones originales. No se elimina ninguna revisión. La lectura y el guardado se serializan fuera del hilo de interfaz. Si falta el archivo principal, está dañado o pierde tareas presentes en el historial, se recuperan las tareas y sus estados de las copias y se muestra un aviso; el archivo anterior se preserva. Una sesión antigua no puede sobrescribir cambios más recientes ni borrar tareas: se ofrece **Reload tasks** y se conserva el borrador. Los errores de lectura o guardado bloquean nuevas modificaciones hasta recargar. HABITAT impide usar una lista temporal al arrancar dentro del sandbox de Codex: abre la aplicación desde el acceso directo de Windows. Las actualizaciones se ejecutan fuera de ese sandbox. El tiempo conserva su lugar en la esquina superior derecha, separado de la barra lateral.

Las comprobaciones de almacenamiento y distribución se ejecutan con `dotnet run --project tests\Habitat.Tasks.Checks`.

`powershell -ExecutionPolicy Bypass -File scripts\check-tasks-ui.ps1` comprueba con controles WinUI reales el panel integrado en el fondo, el foco del campo de creación, el despliegue de tareas terminadas en tema claro y oscuro y la reapertura. Usa tareas sintéticas y un archivo aislado dentro de `artifacts`, abre y cierra su propia ventana y nunca lee ni modifica las tareas del usuario ni conecta el correo o el chat. También comprueba el guardado de ambas listas y la conservación del borrador ante conflictos. Esta compilación de prueba se genera aparte del ejecutable habitual.

## Gmail

El widget superior de la barra lateral muestra el contador de no leídos y tres líneas con remitente, asunto y fragmento del correo. Rota cada **8 segundos** entre los mensajes cargados; se pausa al pasar el ratón, enfocar el mensaje con el teclado o abrir el lector. Durante una conversación o en ventanas estrechas se reduce al icono; el lector se abre hacia la derecha de la barra.

- **Icono y contador:** abren el lector de HABITAT, también disponible en **☰ → Gmail…**. Permite leer texto sin marcar mensajes como leídos, elegir **Inbox unread** o **All unread** y cargar más páginas.
- **Remitente, asunto y fragmento:** abren Gmail en el navegador del sistema y en la cuenta conectada, buscando ese correo por su identificador original. Si no está disponible, se usa el enlace a la conversación.

Las dos zonas tienen su propio marco al pasar el ratón, sin tooltips. La lista se actualiza cada minuto y la rotación no añade consultas a Google. Cada página carga hasta 25 mensajes. El contador de **All unread** es una estimación y aparece con `≈`.

La conexión requiere un cliente OAuth de Google de tipo **Desktop app** con Gmail API habilitada. El uso estándar es gratuito dentro de las cuotas de Google; no necesitas alojar un servidor ni contratar intermediarios. Mantén el proyecto sin facturación vinculada.

HABITAT recupera la autorización del Administrador de credenciales de Windows al abrirse y renueva el acceso automáticamente. Reimportar el mismo cliente conserva la sesión. En modo OAuth **Testing**, Google hace caducar los permisos de renovación a los siete días. **Disconnect** elimina el permiso de renovación local y conserva el cliente para reconectar. Windows protege el cliente y el refresh token en la entrada `HABITAT/Gmail/DesktopAccount/v1`, fuera del repositorio. Las instalaciones anteriores migran automáticamente su archivo cifrado de cuenta; los access tokens temporales se mantienen en memoria.

HABITAT no envía, elimina ni archiva correos, ni comparte su contenido con ChatGPT. Leer dentro de HABITAT no marca mensajes como leídos; abrirlos en Gmail sigue el comportamiento normal de Gmail. La vista de HABITAT usa texto, sin imágenes remotas ni adjuntos.

Las instrucciones completas están en [docs/GMAIL_SETUP.md](docs/GMAIL_SETUP.md). Las comprobaciones sin cuenta real se ejecutan con `dotnet run --project tests\Habitat.Gmail.Checks`.

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

`dotnet run --project tests\Habitat.Destination.Checks --property:BaseOutputPath=C:/proyectos/habitat/artifacts/destination-checks/` verifica el recorrido circular, las URLs de búsqueda, traducción, borradores y mapas, la persistencia de preferencias, los documentos de Notepad y los argumentos codificados de PowerShell. `scripts\check-tasks-ui.ps1` también comprueba los controles WinUI reales: posición a la derecha y centrada en altura, separación, selección exclusiva, conservación de borradores, confirmación/cancelación de Terminal, prevención de ejecuciones duplicadas y recuperación ante fallos. Los lanzadores están simulados en estas pruebas; no abren aplicaciones externas, envían correos, ejecutan comandos ni envían mensajes a ChatGPT.

Con Node.js y el SDK de .NET instalados:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

El comando común ejecuta las comprobaciones de extensión, puente, núcleo, destinos, Gmail, tareas, tiempo y apariencia, comprueba actualizaciones con fallos simulados y compila toda la solución en Release. Sus salidas y datos de tareas quedan bajo `artifacts\verification`. `-NoRestore` reutiliza las dependencias ya restauradas; `-SkipBuild` omite la compilación final. También se ejecuta desde el hook de subida y GitHub Actions. Las comprobaciones de controles WinUI se ejecutan por separado con `scripts\check-tasks-ui.ps1`; usan credenciales en memoria, red y lanzadores bloqueados y rutas explícitas de prueba.

Las comprobaciones del transporte y Gmail usan páginas, respuestas y cuentas sintéticas, con puertos locales aleatorios cuando hace falta. No abren tu conversación ni consultan tu correo real. Las de Gmail incluyen cifrado, reapertura de sesión, renovación automática y construcción de enlaces al correo. El endpoint local `/status`, con la clave de vinculación, ofrece diagnóstico de solo lectura: consultarlo no crea un heartbeat ni consume mensajes.

## Seguridad y límites

- El servidor escucha exclusivamente en `127.0.0.1:8765` y exige una clave aleatoria nueva en cada ejecución.
- No publiques el código de vinculación.
- Los mensajes nuevos van al chat activo de Chrome; selecciona la conversación deseada antes de enviar.
- Los selectores de la extensión dependen de la interfaz web de ChatGPT y pueden requerir mantenimiento cuando ésta cambie.
- Widgets, archivos, imágenes y otras interfaces complejas no se reproducen en HABITAT; consúltalos en Chrome.
- No repitas un envío fallido sin revisar antes Chrome: el mensaje podría haber llegado aunque la confirmación no haya vuelto.

## Checkpoints automáticos en GitHub

Los hooks versionados en `.githooks` trabajan sobre la rama `codex/autosave`. Antes de subir, ejecutan `scripts/verify.ps1` con todas las suites, compilación Release y revisión del diff. El hook `post-commit` envía automáticamente el commit a `origin/codex/autosave`.

Activa los hooks en un clon nuevo con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```

## Documentación y estructura

- [Configuración de Gmail](docs/GMAIL_SETUP.md): OAuth, sesión persistente, coste, privacidad y utilidad de terminal.
- [Estado y dirección del producto](docs/PRODUCT_STATE_AND_VISION.md): funcionalidad implementada, experiencia de uso e ideas pendientes.
- [Arquitectura interna](docs/ARCHITECTURE.md): composición, estados, cierre, almacenamiento y actualizaciones.
- [Contrato del puente](docs/BRIDGE_PROTOCOL.md): mensajes, compatibilidad y transiciones.
- `src/Habitat.App`: ventana, controles WinUI, composición e integración específica con Windows.
- `src/Habitat.Core`: modelos, coordinadores de funcionalidades, servicios y almacenamiento compartidos con las pruebas.
- `extension`: puente con la sesión de ChatGPT en Chrome.
- `tests`: comprobaciones automatizadas sin depender de conversaciones o buzones reales.
- `tools/Habitat.Gmail.Setup`: importación OAuth y comprobación de la cuenta desde terminal.
- `scripts`: compilación, instalación de actualizaciones y checkpoints.
