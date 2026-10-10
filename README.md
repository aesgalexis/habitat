# HABITAT — MVP 0.1

Ventana de escritorio Windows para enviar mensajes **a una pestaña existente de ChatGPT** y mostrar sus respuestas en HABITAT. No usa la API de modelos ni crea otro historial: escribe en la interfaz web de la conversación seleccionada.

## Ejecutar (Windows)

1. Instala Python 3.11+ si no lo tienes.
2. En la carpeta del repositorio ejecuta `python app.py` (en Windows también puede funcionar `py app.py`). No hacen falta paquetes adicionales.
3. En Chrome abre `chrome://extensions`, activa **Modo desarrollador**, elige **Cargar descomprimida** y selecciona la carpeta `extension`.
4. Abre **esta misma conversación** de ChatGPT en Chrome e inicia sesión si hace falta.
5. En HABITAT pulsa **Copiar** junto al código de vinculación. En la pestaña de ChatGPT abre el icono de la extensión, pega el código y pulsa **Conectar esta pestaña**.
6. Escribe en HABITAT y pulsa Enviar (o Ctrl+Enter). La respuesta de texto aparecerá en la ventana, además de quedar guardada en la conversación original de ChatGPT.

**Importante:** deja la pestaña abierta. Puedes estar en otras ventanas. El modelo y la memoria son los que ChatGPT tenga seleccionados en esa conversación. Las respuestas con widgets, archivos, imágenes u otras UI complejas no se reproducen aquí; consúltalas en Chrome.

## Límites y seguridad

- Prueba experimental, **no comprobada aún contra la versión concreta de ChatGPT que tienes abierta**. Sus selectores HTML pueden cambiar; si falla, revisa `extension/content.js`.
- La extensión puede interactuar con tu sesión ChatGPT, por lo que conviene instalarla solo desde código que hayas revisado. No publiques el código de vinculación.
- El servidor escucha **solo en 127.0.0.1:8765** y exige una clave aleatoria nueva en cada ejecución. No expone una API pública. Si reinicias HABITAT, vuelve a pegar el nuevo código en la extensión.
- No intentes enviar otra pregunta hasta que la anterior termine. Hay un timeout de 3 minutos. Un mensaje enviado que falle a mitad podría haber llegado igualmente a ChatGPT: comprueba Chrome antes de repetirlo.
- No garantiza representar estados complejos (herramientas, preguntas de confirmación, streaming, cambios de modelo, contenido no textual). Tampoco hay garantía de estabilidad ni soporte oficial para esta automatización.
- El contenido de la conversación seguirá en ChatGPT. HABITAT solo presenta copias del texto mientras se ejecuta.

## Siguiente paso

Una vez validado el puente en Windows, sustituir Tkinter por una superficie visual más cuidada (por ejemplo Tauri) y añadir un fondo dinámico. Primero validar que la comunicación funciona.
