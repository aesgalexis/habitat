# Mercados

El widget **Markets / Mercados** se añade, retira y ordena desde **Preferences → Sidebar / Preferencias → Barra lateral**. Está desactivado en instalaciones nuevas; los perfiles anteriores conservan su posición debajo de Tareas hasta personalizarla. La barra recogida conserva el icono y la variación porcentual. Al desplegarla muestra el nombre, la cotización en la moneda de origen, la variación diaria y la hora del dato. Su nombre accesible y tooltip incluyen la fecha completa de la cotización. Véase [Barra lateral](SIDEBAR.md).

Al pulsarlo abre un panel con los favoritos, precios, variación diaria, gráfica del último mes y hasta tres titulares con fecha y dominio de origen. Los enlaces abren Chrome mediante el lanzador habitual de HABITAT. Los titulares conservan el texto original del proveedor; la interfaz, los estados y la accesibilidad siguen el idioma de Preferencias.

## Favoritos

**Preferences → Markets / Preferencias → Mercados** permite añadir y quitar hasta cinco símbolos de Yahoo Finance y fijar uno en la barra lateral. **Rotate favorites / Alternar favoritos** recorre la lista cada ocho segundos. La rotación se pausa con el puntero o el foco encima, durante una actualización o mientras hay un diálogo abierto.

Los valores iniciales son `^GSPC` (S&P 500), `^IBEX` (IBEX 35), `EURUSD=X` (euro/dólar) y `BTC-USD` (bitcoin/dólar). Se pueden usar otros símbolos, por ejemplo `AAPL`, `MSFT` o `IBE.MC`. La validez y disponibilidad de cada símbolo dependen del proveedor. Una lista vacía se conserva y ofrece gestionar favoritos. Cerrar Preferencias guarda los cambios atómicamente junto con el resto de la configuración.

## Fuentes y actualización

Se consultan los datos públicos de Yahoo Finance: `query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&range=1mo` y `feeds.finance.yahoo.com/rss/2.0/headline`. No requieren una clave de API en esta implementación. El acceso no constituye una API contratada; el proveedor puede cambiar o limitar estos endpoints. Las cotizaciones pueden llevar retraso y se muestra su fecha de origen, sin afirmar que sean datos en tiempo real.

Con el widget activado, la aplicación consulta los favoritos al arrancar y después cada quince minutos. Al retirarlo deja de iniciar consultas periódicas, conservando sus favoritos. Ante fallos conserva la última cotización válida, la marca como pendiente de actualización y programa otro intento tras cinco minutos. Los fallos de un símbolo no descartan los demás. El botón de actualización permite reintentar manualmente. Los titulares se solicitan al abrir o seleccionar un activo y se guardan en memoria hasta quince minutos; la caché está limitada a diez combinaciones de símbolo e idioma. No se guardan precios ni noticias en el perfil del usuario.

La variación diaria utiliza el porcentaje del proveedor cuando existe. En su ausencia se calcula a partir del cierre anterior a la fecha del dato en la zona del mercado. No utiliza `chartPreviousClose` de una petición mensual, porque corresponde al principio del periodo de la gráfica. Si no hay una referencia válida se muestra un guion.

## Preguntar en el chat

**Explain this movement / Explícame este movimiento** prepara una pregunta con el activo, precio, variación, fecha UTC, enlace a la fuente y serie diaria. Usa el destino OpenAI o Codex seleccionado si está habilitado, o uno de esos destinos habilitados. Conserva cualquier borrador previo y añade la pregunta al final. El usuario revisa y envía el contenido desde el editor. Si ambos destinos están deshabilitados, el botón explica cómo activarlos.

El panel de Mercados es independiente de los destinos y no fuerza su vista de resultados. Cerrar el panel devuelve el foco al editor.

## Verificación

`Habitat.Core.Checks` cubre normalización, persistencia, cálculo de variaciones, datos incompletos, selección fija, rotación, fallos parciales, intervalos, cambios de lista durante una consulta y cancelación. Las pruebas de RSS rechazan entidades externas y enlaces que no sean HTTPS.

La compilación con `HabitatUiChecks=true` admite `--markets-ui-only`; también forma parte de `--all-ui-checks`. Comprueba los controles WinUI en ambos idiomas, los dos estados del sidebar, el espacio para Tareas, apertura del panel, conservación del borrador y edición de favoritos. Usa respuestas sintéticas y almacenamiento aislado dentro de `artifacts`.
