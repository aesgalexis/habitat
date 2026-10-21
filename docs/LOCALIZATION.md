# Idiomas de interfaz

HABITAT admite **English** y **Español**. En **Preferences… → Appearance → Interface language** se elige el idioma; en español, **Preferencias… → Apariencia → Idioma de la interfaz**. El cambio se guarda y se aplica al cerrar el diálogo, sin reiniciar ni borrar borradores o resultados. Los perfiles anteriores conservan el inglés. Un valor de idioma desconocido también utiliza inglés.

## Textos y datos

- `src/Habitat.Core/Localization/en.json` y `es.json` contienen las mismas claves estables. `UiText.Get` obtiene un texto y `UiText.Format` aplica argumentos con la cultura de interfaz. No concatenar fragmentos cuya gramática dependa del idioma.
- `UiLanguage.Watch`, `Value` y los métodos de accesibilidad registran propiedades que deben actualizarse. Sus funciones solo deben leer estado y resolver textos: nunca enviar mensajes, ejecutar comandos, abrir enlaces o copiar contenido. Las referencias débiles permiten recoger controles retirados; si otra asignación reemplaza una etiqueta, la actualización anterior no debe sobrescribirla.
- Los IDs de destinos, categorías, protocolos, estados de ejecución y valores persistidos no se traducen. Las fechas visibles usan `UiText.Culture`; las URLs, JSON, cabeceras y formatos de intercambio conservan su cultura explícita.
- Los textos del usuario, nombres de aplicaciones añadidas, correos, resultados de búsqueda, traducciones, respuestas de asistentes y salidas de comandos se conservan. El idioma de interfaz no cambia las instrucciones de ChatGPT ni el idioma de destino de Translate.
- El alcance es la aplicación de escritorio. Las interfaces de Chrome, ChatGPT, Codex y las extensiones tienen su propia configuración de idioma. Al indicar una acción en una herramienta externa, conservar su etiqueta real.

## Comprobaciones

`node tests/localization.checks.cjs` detecta claves utilizadas que falten y comprueba la correspondencia de los catálogos. `Habitat.Core.Checks` valida los argumentos de formato, la migración y persistencia de preferencias y la estabilidad de IDs y culturas de protocolo.

Mercados utiliza las claves `Markets*` de ambos catálogos, incluidas sus preferencias, estados de red y pregunta preparada en el editor. Los símbolos son IDs estables y los titulares conservan el idioma original de la fuente. `--markets-ui-only` comprueba ambos idiomas con datos sintéticos; véase [Mercados](MARKETS.md).

La compilación WinUI con `HabitatUiChecks=true` admite `--language-ui-only`; `--all-ui-checks` también la incluye. Comprueba ambos idiomas, guardado, accesibilidad, categorías, estructura inicial, borradores y una traducción pendiente y terminada. Sus servicios y archivos son sintéticos y están aislados dentro de `artifacts`.
