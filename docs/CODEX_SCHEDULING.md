# Mensajes programados para Codex

Desde 0.15.1, el icono de calendario junto a **Enviar a Codex / Send to Codex** permite elegir el mensaje, el chat de destino y una fecha y hora locales. Está disponible también con el editor vacío para gestionar la cola. El envío normal mantiene su comportamiento.

El selector comienza con chats creados desde HABITAT. **Incluir otros chats locales de Codex / Include other local Codex chats** amplía la lista para elegir explícitamente otro destino; esa elección autoriza solo el mensaje programado y no incorpora ese chat al historial normal de HABITAT. Se muestran primero aquellos cuyo último turno finalizó con el error de límite de uso o un aviso equivalente en inglés o español. Una mención del usuario o un fallo antiguo seguido de otro turno no los marca como limitados.

**Usar la hora de recuperación de la cuenta** propone una hora posterior al reinicio de los límites agotados expuestos por Codex. Si no hay un timestamp estructurado fiable, el botón queda desactivado y se elige la hora manualmente. No se deduce una fecha a partir de frases ambiguas. La disponibilidad del modelo sigue dependiendo de Codex y de la cuenta. La recomendación usa el último reset entre ventanas agotadas de la cuenta, por lo que puede ser conservadora para un chat concreto.

## Ejecución y gestión

- HABITAT debe permanecer abierto y Windows con una sesión interactiva iniciada. El equipo suspendido o apagado no ejecuta la programación. Los mensajes vencidos hace más de cinco minutos quedan para revisión y reprogramación; dentro de esa tolerancia se ejecutan al siguiente tick.
- La programación fija el identificador del chat. Cambiar después la conversación visible no cambia el destino. Al vencer, HABITAT abre ese chat, comprueba su identidad y que el editor esté vacío, y usa el mecanismo habitual de entrega verificada. Los títulos duplicados impiden identificar el destino y requieren revisión.
- Si hay un turno en ejecución se espera, comprobando de nuevo tras treinta segundos. Un reset de cuenta conocido pospone el intento hasta después de esa hora. El diálogo muestra la próxima comprobación. No se sustituye un borrador escrito directamente en Codex.
- La cola se guarda atómicamente en `%LOCALAPPDATA%\HABITAT\codex-scheduled-messages.json`, con texto original, destino, hora UTC y estado. Persiste a través de cierres y actualizaciones. No sincroniza entre PCs.
- **Editar programación** cambia el mensaje, destino u hora; **Cancelar** impide su ejecución. Los registros enviados, cancelados o revisables pueden retirarse de la lista. Los textos permanecen locales hasta el envío a Codex.
- Antes de intentar enviar se guarda **Enviando**. Un cierre inesperado, cancelación durante la entrega o resultado sin confirmar pasa a **Requiere revisión** y nunca se reenvía automáticamente. Hay que comprobar Codex antes de reprogramarlo. **Enviado** confirma la entrega, no la finalización correcta del trabajo de Codex.
- Un archivo ilegible, corrupto o lleno bloquea nuevos envíos sin sobrescribirlo ni borrar el borrador. La cola admite hasta 500 registros dentro de 16 MiB; se pueden retirar los registros terminados.

## Implementación y pruebas

`CodexScheduleQueue` conserva una cola independiente de la interfaz y evita solapamientos. El temporizador existente consulta la cola cada dos segundos; las operaciones participan en el drenaje de cierre y la actualización espera cualquier entrega programada en curso. La detección y el envío normal no descargan ni crean programaciones.

`CodexReadClient` añade paginación de `thread/list`, conserva `codexErrorInfo` y permite `account/rateLimits/read`, siempre de solo lectura. El canal no reanuda hilos ni inicia turnos. Los contratos se contrastaron con [la documentación oficial del app-server de Codex](https://learn.chatgpt.com/docs/app-server). La entrega programada navega al chat elegido por su URI y vuelve a verificar la selección mediante el lector y los controles de escritorio ya utilizados por HABITAT.

`Habitat.Destination.Checks` comprueba persistencia, cancelación, revisión tras reinicio, fallo de guardado, duplicados, horarios, pausas por límites y turnos activos, protección de borradores, identidad del destino, separación de la autorización ordinaria y el protocolo de lectura paginada. `--codex-schedules-ui-only` y `--all-ui-checks` comprueban inglés y español, el icono junto al envío, el formato inicial común, cancelación de carga, selección prioritaria, hora propuesta, edición y cancelación. Usan datos aislados y servicios simulados; no envían mensajes reales.
