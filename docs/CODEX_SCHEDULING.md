# Mensajes programados para Codex

Desde 0.15.1, el icono de calendario junto a **Enviar a Codex / Send to Codex** permite elegir el mensaje, el chat de destino y una fecha y hora locales. Está disponible también con el editor vacío para gestionar la cola. El envío normal mantiene su comportamiento.

Desde 0.15.4, **Nuevo chat de Codex / New Codex chat** permite programar el primer mensaje aunque todavía no haya chats de HABITAT. El chat se crea al llegar la hora, mediante el envío habitual de HABITAT; guardar la programación no crea ni envía nada en Codex. Tras confirmar su identidad y primera instrucción, se registra para futuros envíos. Esta opción también está disponible si falla la carga de chats existentes; editar una programación existente nunca cambia su destino automáticamente.

Desde 0.15.2, el selector admite exclusivamente chats creados desde HABITAT, con el mismo registro de origen que el envío normal. No permite incluir otras conversaciones locales. Se muestran primero aquellos cuyo último turno finalizó con el error de límite de uso o un aviso equivalente en inglés o español. Una mención del usuario o un fallo antiguo seguido de otro turno no los marca como limitados.

La entrega comprueba el origen antes de leer el historial, abrir el chat o enviar. Una programación externa guardada por una versión anterior no se ejecuta: queda como no enviada para revisión o para elegir un chat válido.

**Usar la hora de recuperación de la cuenta** propone una hora posterior al reinicio de los límites agotados expuestos por Codex. Si no hay un timestamp estructurado fiable, el botón queda desactivado y se elige la hora manualmente. No se deduce una fecha a partir de frases ambiguas. La disponibilidad del modelo sigue dependiendo de Codex y de la cuenta. La recomendación usa el último reset entre ventanas agotadas de la cuenta, por lo que puede ser conservadora para un chat concreto.

## Ejecución y gestión

Desde 0.15.5, el calendario muestra el número de mensajes pendientes (hasta **99+**); su tooltip y etiqueta accesible indican el próximo envío, incluida una espera por recuperación de la cuenta. Los enviados, cancelados y revisables no suman al contador. La señal **!** ámbar indica mensajes que requieren revisión o un problema de almacenamiento; se mantiene hasta resolverlo, reprogramar o retirar el registro correspondiente. Estos indicadores se reconstruyen al abrir HABITAT y siguen el idioma elegido.

Guardar muestra una confirmación durante cinco segundos junto al calendario y conserva el foco en el editor. Al abrir el calendario con el editor vacío, la lista de programaciones aparece antes del formulario para facilitar su consulta, edición y cancelación.

Desde 0.15.6, un acuse de recepción correcto de Codex mantiene el envío como **Enviado**, aunque falle la vinculación posterior del chat o la lectura de su historial. Esa vinculación no puede generar una revisión ni reintentar un mensaje ya entregado. El cotejo del historial admite únicamente el salto de línea final añadido por Codex; la comprobación del editor sigue conservando exactamente el texto y los espacios del usuario. La lista principal muestra pendientes, envíos en curso y revisiones; los enviados y cancelados quedan en **Mensajes enviados y cancelados**, cerrado por defecto.

- HABITAT debe permanecer abierto y Windows con una sesión interactiva iniciada. El equipo suspendido o apagado no ejecuta la programación. Los mensajes vencidos hace más de cinco minutos quedan para revisión y reprogramación; dentro de esa tolerancia se ejecutan al siguiente tick.
- Para un chat existente, la programación fija su identificador. Cambiar después la conversación visible no cambia el destino. Al vencer, HABITAT abre ese chat, comprueba su identidad y que el editor esté vacío, y usa el mecanismo habitual de entrega verificada. Los títulos duplicados impiden identificar el destino y requieren revisión. Para **Nuevo chat de Codex**, crea un chat separado sin enviar al que esté visible.
- Si hay un turno en ejecución se espera, comprobando de nuevo tras treinta segundos. Un reset de cuenta conocido pospone el intento hasta después de esa hora. El diálogo muestra la próxima comprobación. No se sustituye un borrador escrito directamente en Codex: para un chat nuevo también se comprueba que su editor no contenga un borrador antes de navegar.
- La cola se guarda atómicamente en `%LOCALAPPDATA%\HABITAT\codex-scheduled-messages.json`, con texto original, destino, hora UTC y estado. El formato 2 distingue explícitamente un chat nuevo de un destino existente y conserva la lectura del formato 1. Un destino ausente o dañado nunca se interpreta como permiso para crear un chat. Persiste a través de cierres y actualizaciones. No sincroniza entre PCs.
- **Editar programación** cambia el mensaje, destino u hora; **Cancelar** impide su ejecución. Los registros enviados, cancelados o revisables pueden retirarse de la lista. Los textos permanecen locales hasta el envío a Codex.
- Antes de intentar enviar se guarda **Enviando**. Un cierre inesperado, cancelación durante la entrega o resultado sin confirmar pasa a **Requiere revisión** y nunca se reenvía automáticamente. Hay que comprobar Codex antes de reprogramarlo. **Enviado** confirma la entrega, no la finalización correcta del trabajo de Codex.
- Un archivo ilegible, corrupto o lleno bloquea nuevos envíos sin sobrescribirlo ni borrar el borrador. La cola admite hasta 500 registros dentro de 16 MiB; se pueden retirar los registros terminados.

## Implementación y pruebas

`CodexScheduleQueue` conserva una cola independiente de la interfaz y evita solapamientos. El temporizador existente consulta la cola cada dos segundos; las operaciones participan en el drenaje de cierre y la actualización espera cualquier entrega programada en curso. La detección y el envío normal no descargan ni crean programaciones.

`CodexReadClient` añade paginación de `thread/list`, conserva `codexErrorInfo` y permite `account/rateLimits/read`, siempre de solo lectura. El canal no reanuda hilos ni inicia turnos. Los contratos se contrastaron con [la documentación oficial del app-server de Codex](https://learn.chatgpt.com/docs/app-server). La entrega programada navega al chat elegido por su URI y vuelve a verificar la selección mediante el lector y los controles de escritorio ya utilizados por HABITAT.

`Habitat.Destination.Checks` comprueba persistencia, cancelación, revisión tras reinicio, fallo de guardado, duplicados, horarios, pausas por límites y turnos activos, protección de borradores, identidad y origen del destino, rechazo de programaciones externas guardadas previamente y el protocolo de lectura paginada. `--codex-schedules-ui-only` y `--all-ui-checks` comprueban inglés y español, el icono junto al envío, el formato inicial común, cancelación de carga, selección prioritaria, hora propuesta, edición y cancelación. Usan datos aislados y servicios simulados; no envían mensajes reales.
