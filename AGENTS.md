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

## Idioma de la interfaz

- HABITAT admite inglés y español como idiomas de interfaz. Todos los textos visibles y de accesibilidad deben usar el idioma elegido en Preferences, con inglés como valor inicial y respaldo.
- Centraliza títulos, botones, menús, campos, placeholders, tooltips, estados vacíos, avisos, errores y etiquetas de accesibilidad en los catálogos de `src/Habitat.Core/Localization`. Mantén el idioma también durante foco, carga, errores y recuperación.
- El contenido escrito por el usuario se conserva en su idioma original. No traduzcas tareas, mensajes, nombres u otros datos aportados por el usuario.
- Antes de entregar nuevos elementos o modificar existentes, revisa ambos idiomas. Usa identificadores internos estables independientes de las etiquetas traducidas. Actualiza las comprobaciones y la documentación cuando cambien las etiquetas.
- Esta regla se aplica al producto; la documentación del proyecto y la conversación con el usuario pueden estar en español.

## Pantalla inicial común de los destinos

- La pantalla inicial de todos los destinos mantiene siempre el mismo formato y disposición de tres elementos: indicación de la acción, editor con cursor y botón para ejecutar/enviar esa acción.
- Conserva la indicación sobre el editor, el editor centrado y el botón de acción debajo. Solo cambian los textos correspondientes al destino y al idioma elegido (por ejemplo: **Draft a new email…**, cursor y **Draft email**, o **Redacta un nuevo correo…**, cursor y **Crear borrador**).
- No añadas explicaciones de uso, resultados, estados informativos ni barras de acciones de una sección a esta pantalla inicial. La sección con resultados y sus acciones aparece tras ejecutar la acción; limpiar debe devolver la pantalla inicial y el foco al editor.
- Verifica esta estructura compartida al crear o modificar un destino; no fuerces la vista de resultados ni desplaces el editor al pie antes de la primera acción.

## Actualización de la aplicación durante cambios

- El usuario autoriza cerrar HABITAT cuando sea necesario para compilar o instalar cambios en este proyecto. No pidas confirmación adicional para cerrar la aplicación durante este trabajo.
- Intenta primero el cierre normal de la instancia de HABITAT de este proyecto. Si no responde, puedes finalizar esa misma instancia para liberar los archivos del ejecutable.
- Inicia y reinicia la aplicación real desde la sesión normal de Windows, fuera del sandbox de ejecución de Codex. El sandbox cambia LocalApplicationData y sus datos temporales pueden desaparecer: no lances allí la aplicación que usará el usuario.
- La compilación y las pruebas pueden ejecutarse dentro del sandbox. Las pruebas de tareas deben usar una ruta de almacenamiento explícita y aislada dentro de artifacts, nunca la lista real del usuario.
