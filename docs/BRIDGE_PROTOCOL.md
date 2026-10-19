# Contrato del puente local

Versión de transporte de escritorio: **1**. Se anuncia en `/pair`, `/status` y los ticks WebSocket. Los campos adicionales se pueden ignorar. La extensión ya instalada sigue siendo compatible; no se exige un nuevo emparejamiento ni una versión nueva de extensión. El protocolo `6` entre worker y `content.js` es una frontera diferente.

El servidor escucha solo en `127.0.0.1:8765`. `/pair` requiere `X-Habitat-Pair: 1` y devuelve la clave efímera de esta ejecución. Los endpoints HTTP restantes requieren `X-Habitat-Key`; WebSocket requiere un primer mensaje `{kind: "auth", key: "..."}`. Esto evita acceso web ordinario sin autorización, pero no pretende proteger frente a software local con acceso equivalente al usuario.

El worker recibe `{kind: "tick", protocolVersion: 1, message: null | {id, text}}`. Envía `{kind: "heartbeat", ready, issue, model}`. Solo una confirmación reciente de la página permite considerar conectada la extensión; recibir ticks sin heartbeat no renueva esa disponibilidad. `/next` mantiene la compatibilidad HTTP anterior.

| Estado | Entrada | Resultado |
| --- | --- | --- |
| Idle | Encolar con página disponible | Queued; nuevo ID y texto exacto. |
| Queued | Tick o `/next` | Ofrecer el mismo mensaje hasta acknowledgement. |
| Queued | `received` con ID correcto | Received; detener reentrega. |
| Queued / Received | `started` con ID correcto | Responding; detener reentrega. |
| Responding | `received` tardío | Conservar Responding. |
| Cualquier solicitud activa | `response` / `error` con ID correcto | Idle; emitir una única finalización aceptada. |
| Solicitud activa | Más de cuatro minutos desde encolado | Idle; mantener el aviso existente de revisar Chrome. |
| Sin solicitud / ID incorrecto o vencido | Evento de entrega | Ignorar. |

`status` y `model` son eventos informativos y no completan solicitudes. Las confirmaciones se envían por `/event` con las propiedades `type`, `id`, `text`, `model`. Los fixtures de `tests/fixtures/bridge-v1.json` se verifican tanto en C# como en JavaScript.

Se mantienen los plazos existentes: disponibilidad de página de 4 segundos, ticks de 1,2 segundos, espera máxima de página de 2 segundos, respuesta observada en Chrome de 3 minutos y timeout de escritorio de 4 minutos. Las pruebas de la máquina de estados usan `TimeProvider` para avanzar el reloj sin esperar.

`/status` es de solo lectura: no confirma ni consume mensajes. Incluye `protocolVersion`, `processId`, `hasMessageInFlight`, `deliveryPhase`, conectividad y diagnóstico. El actualizador verifica que el proceso corresponde a la versión activa antes de esperar y cerrar; no analiza el título de la ventana.

## Apertura externa explícita

HABITAT utiliza Chrome para los destinos web y la extensión. Los cuatro canales WebSocket y `/next` y `/event` autentican la clave local sin consultar la asociación HTTPS de Windows ni una preferencia de navegador. El campo opcional `browser` y la cabecera `X-Habitat-Browser` enviados por compilaciones anteriores se ignoran, por lo que esas extensiones se reconectan sin cambiar opciones.

Browser Bridge 0.9.0 exige consentimiento local explícito en su página de conexión antes de obtener una clave, conectar cualquiera de los cuatro canales o leer el contenido de una página por una solicitud del escritorio. El botón **Enable connection** guarda `habitatConsentVersion: 1`; **Disconnect** cierra los cuatro canales y evita nuevos trabajos. No cambia los paquetes del protocolo. La política de privacidad incluida describe los datos, almacenamiento y el salto local HTTP/WebSocket sin cifrado.

La extensión 0.7.1 añade `supportsExternalOpen: true` al heartbeat. Los mensajes incluyen `openExternally` (false por defecto); solo true permite seleccionar la pestaña vinculada y restaurar/enfocar su ventana antes de entregar al editor. El botón principal mantiene el comportamiento en segundo plano. Una extensión anterior puede seguir enviando normalmente; el escritorio rechaza un envío externo antes de encolarlo y pide **Reload the HABITAT browser extension to send and open ChatGPT.** si no anuncia esa capacidad.

Para abrir la conversación sin enviar texto, el tick añade `openChatId`; el worker devuelve `{kind: "chat-opened", id, opened}` por el socket autenticado. No se crea mensaje ni se cambia de conversación. La solicitud vence a los cinco segundos. El worker conserva hasta 128 aperturas recientes para confirmar una reentrega sin volver a enfocar. Las pruebas verifican que los envíos normales no activan ventanas y que una apertura sin texto nunca entrega un mensaje.

## Traducción mediante la página de Chrome

`/translate-bridge` es un canal WebSocket independiente, autenticado con el primer paquete `{key, translationProtocol: 1}`. No necesita un chat activo ni modifica la vinculación de ChatGPT. El escritorio envía `{kind: "translation-tick", request: null | {id, text, target}}`; el worker confirma actividad con `translation-pong` y devuelve `{kind: "translation-result", id, text}` o un código `error`. Solo se acepta el propietario actual y el ID del trabajo pendiente. Los frames de este canal se limitan a 65.536 bytes; el canal anterior mantiene su límite de 8.192 bytes. `translationConnected` en `/status` indica su disponibilidad sin crear actividad artificial.

El campo `target` solo admite `en`, `el` y `es`. Los trabajos no invocan una API de traducción: el worker rellena Google Translate y lee el resultado de su DOM. Las solicitudes canceladas dejan de ofrecerse; el worker comprueba el trabajo actual antes de devolver un resultado. Se conserva como máximo una respuesta reciente en memoria para evitar traducir dos veces por una reentrega. Las traducciones no generan eventos de conversación ni mensajes en ChatGPT.

El escritorio limita cada espera de traducción a 55 segundos y libera el trabajo al vencer el plazo para permitir reintentos. La sección permite cancelar inmediatamente, aunque una operación del navegador quede pendiente. El worker devuelve el resultado después de liberar la emulación de foco de la página, sin seleccionar pestañas ni cambiar ventanas de Chrome. El campo opcional `diagnostic` de una respuesta correcta contiene únicamente versión de extensión, ID de pestaña e indicador de ejecución en segundo plano; no contiene el texto original ni la traducción.

## Búsqueda de Google mediante Chrome

`/search-bridge` es independiente de la conversación y de Translate. Se autentica con `{key, searchProtocol: 1}`. El escritorio envía `{kind: "search-tick", request: null | {id, query, start}, reset: null | id}` cada 700 ms; el worker responde `search-pong` y `{kind: "search-result", id, results: [{title, url, snippet}], nextStart: null | number}` o `{kind: "search-result", id, error}`. Solo el propietario actual puede completar el ID pendiente. Los frames se limitan a 262.144 bytes y `searchConnected` en `/status` informa de este canal sin simular actividad.

`Reset()` invalida el trabajo actual y conserva una orden `reset` con ID hasta recibir `{kind: "search-reset", id, closed: boolean}` del propietario autenticado. `closed` indica si se eliminó la pestaña propia; `false` también permite completar la limpieza si ya estaba cerrada o se había navegado a otra web. Mientras exista un reset pendiente, el snapshot atómico del tick envía `request: null`, aunque se haya solicitado una búsqueda nueva. Así el cierre anterior no alcanza la pestaña de esa nueva búsqueda. La orden se reentrega al reconectar o ante fallos de cierre. El worker serializa el cierre después de las creaciones de pestaña pendientes y conserva una confirmación reciente para responder a reentregas sin cerrar una pestaña posterior. Esta función requiere la extensión 0.6.1 o posterior.

Las consultas admiten hasta 2.048 caracteres, offsets entre 0 y 990 y hasta 30 resultados por página. Los títulos admiten 600 caracteres, los fragmentos 2.000 y las URLs HTTP(S) 8.192, sin credenciales. El siguiente offset debe ser mayor que el actual. El escritorio limita la espera a 55 segundos y elimina el trabajo al cancelar, desconectar o vencer el plazo. El worker no publica respuestas canceladas y conserva una sola respuesta reciente para evitar repetir consultas por reentrega. No se guardan consultas ni resultados en disco.

Los códigos de recuperación son `consent`, `blocked`, `permission`, `page-changed`, `timeout` y `result`. La extensión lee únicamente resultados web de la página real, en una pestaña propia sin activarla. La búsqueda nunca utiliza el canal de mensajes de ChatGPT ni integra Google Maps.

## Búsqueda de YouTube

`/youtube-bridge` usa el mismo contrato de búsqueda (`searchProtocol: 1`, ticks, resultados y reset con confirmación) con una instancia completamente independiente del servicio. `youtubeConnected` informa de la conexión en `/status`. Un ID de Google no completa un trabajo de YouTube ni su reset, aunque los sockets compartan la misma clave de emparejamiento. Requiere la extensión 0.7.0 o posterior y el permiso `https://www.youtube.com/results*`.

`start` indica el índice de vídeos dentro de los resultados cargados en la página. El worker devuelve bloques de hasta 20 vídeos y `nextStart` avanza en esa lista mediante la continuación de YouTube, sin llamar a su API. En `start: 0` puede anteponer hasta diez canales cuyo nombre o identificador coincida con la consulta; no cuentan para `nextStart`. Admite URLs canónicas `https://www.youtube.com/watch?v=<ID de 11 caracteres>` y, solo en la primera página, enlaces de canal de `https://www.youtube.com` con rutas `/@…`, `/channel/UC…`, `/c/…` o `/user/…`, sin parámetros ni fragmentos. No se añade ningún campo al contrato: el escritorio reconoce los canales por su URL validada. `snippet` conserva canal, duración, metadatos y descripción como texto; en canales contiene su identificador, metadatos y descripción públicos cuando estén disponibles. La interfaz, solicitudes pendientes y la pestaña propia se reinician únicamente para ese proveedor.

Los códigos adicionales `background-permission` y `background-unavailable` permiten recuperar fallos al mantener activa la página en segundo plano. El worker libera la emulación de foco antes de devolver el resultado. El campo opcional `diagnostic` contiene únicamente etapas y contadores del lector, sin consultas, títulos ni contenido de usuario.
