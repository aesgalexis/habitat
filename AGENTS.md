# Instrucciones de trabajo para HABITAT

Estas reglas se aplican a todo el repositorio.

## Control visual

- No uses control visual, automatización de interfaz, Computer Use, Sky ni herramientas equivalentes por defecto.
- Prioriza la inspección del código, pruebas automatizadas, logs y comandos no interactivos.
- Usa control visual únicamente cuando sea realmente necesario para completar o verificar la tarea, o cuando el usuario lo solicite expresamente.
- Antes de usarlo por necesidad técnica, explica brevemente por qué no basta una comprobación no visual.
- No uses control visual como comprobación rutinaria si el mismo resultado puede verificarse de forma fiable mediante código o pruebas automatizadas.

## Agentes y delegación

- No crees subagentes ni delegues trabajo por defecto, aunque la tarea sea grande o admita paralelización.
- Solo puedes crear agentes si el usuario lo solicita expresamente.
- También puedes proponer su uso, pero debes pedir permiso y esperar una autorización explícita antes de crear cualquier agente.
- Una autorización anterior no se reutiliza automáticamente para tareas futuras: si no está claro que sigue vigente, vuelve a pedir permiso.
