# Idioma de la interfaz

Todos los textos de interfaz de HABITAT se muestran en **inglés**, incluidos botones, menús, carga, errores, recuperación, tooltips y etiquetas accesibles. Es la regla vigente de [AGENTS.md](../AGENTS.md).

Los perfiles de las versiones bilingües se cargan en inglés conservando sus demás preferencias. No hay un selector de idioma en Appearance. Los catálogos históricos se conservan para reconocer mensajes antiguos y mantener la compatibilidad del código recuperado; UiText resuelve siempre el catálogo inglés para el producto.

El contenido del usuario y de los proveedores conserva su idioma: tareas, mensajes, nombres, borradores, títulos y resultados. Los idiomas de destino de Translate son independientes del idioma de interfaz.

Las comprobaciones de Core y de la vista verifican la migración de perfiles antiguos, etiquetas accesibles, la estructura inicial centrada y la conservación de borradores y traducciones en curso. La comprobación tests/localization.checks.cjs valida las referencias del código a los recursos.
