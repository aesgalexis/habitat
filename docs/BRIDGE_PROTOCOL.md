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
