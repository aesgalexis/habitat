# Idioma de la interfaz

Estado vigente: **interfaz en inglés**, según [AGENTS.md](../AGENTS.md). Revisión documental: 24 de octubre de 2026. La documentación y la conversación de desarrollo pueden estar en español.

## Textos y datos

- Títulos, botones, menús, campos, placeholders, tooltips, accesibilidad, carga, errores y recuperación deben utilizar inglés en todos sus estados.
- Conserva el vocabulario existente: **Tasks**, **New task**, **Completed**, **Reload tasks**. Al documentar una acción, cita su etiqueta real en inglés.
- No traduzcas tareas, mensajes, nombres, correos, resultados, respuestas ni otros datos escritos por el usuario o recibidos de proveedores.
- Translate permite elegir el idioma de la traducción; eso no cambia el idioma de la interfaz.
- Las aplicaciones externas mantienen su propia configuración. No cambies sus etiquetas ni su configuración como parte de la documentación de HABITAT.

## Verificación

Al cambiar un componente, revisa sus textos visibles y accesibles, incluidos estados vacíos, foco, carga, error y reintento. Actualiza las comprobaciones y la documentación asociadas a cualquier etiqueta modificada; consulta [CHECKS.md](CHECKS.md).

La versión anterior de esta guía describía catálogos `en.json`/`es.json`, `UiText`, `UiLanguage` y una prueba de localización que no existen en esta copia. Esa descripción no constituye una implementación ni una autorización para introducir interfaz bilingüe. Cualquier propuesta futura requiere reconciliar primero la regla del repositorio. Véase [PROJECT_STATUS.md](PROJECT_STATUS.md).
