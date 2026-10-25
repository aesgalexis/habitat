# Decisiones del proyecto

Registro breve de decisiones vigentes a 24 de octubre de 2026. Recoge reglas y evidencias existentes; no introduce nuevas autorizaciones. Las ideas de producto permanecen en [PRODUCT_STATE_AND_VISION.md](PRODUCT_STATE_AND_VISION.md).

| Decisión | Motivo y consecuencia | Referencia principal |
| --- | --- | --- |
| Interfaz en inglés; contenido del usuario intacto | Mantener vocabulario coherente en controles, errores y accesibilidad. La documentación puede estar en español. | [AGENTS.md](../AGENTS.md), [LOCALIZATION.md](LOCALIZATION.md) |
| Pantalla inicial común de tres elementos | Indicación sobre el editor, editor centrado y botón debajo. Resultados y sus acciones aparecen después de ejecutar. | [AGENTS.md](../AGENTS.md) |
| Chrome para las integraciones web | La extensión y los adaptadores se apoyan en la sesión real de Chrome. No hay selector de navegador vigente. | [BRIDGE_PROTOCOL.md](BRIDGE_PROTOCOL.md), [ARCHITECTURE.md](ARCHITECTURE.md) |
| Conservar la conversación original | ChatGPT mantiene su historial; HABITAT entrega y muestra texto mediante el puente. Los cambios del proveedor requieren mantenimiento. | [USER_GUIDE.md](USER_GUIDE.md), [BRIDGE_PROTOCOL.md](BRIDGE_PROTOCOL.md) |
| Pruebas aisladas y comprobación no visual por defecto | Evitar que una verificación lea o modifique tareas, cuentas y sesiones reales. | [AGENTS.md](../AGENTS.md), [CHECKS.md](CHECKS.md) |
| Arranque de uso diario desde Windows normal | El sandbox puede cambiar el perfil y los datos temporales. | [AGENTS.md](../AGENTS.md), [CONTRIBUTING.md](../CONTRIBUTING.md) |
| SignPath Foundation elegida y solicitud aplazada desde el 19 de octubre | Solo preparación local hasta que el propietario retome el proceso; no activar cuentas, firma ni workflows. | [SIGNING.md](SIGNING.md) |
| Extensiones versionadas por separado | Escritorio, Chrome y VS Code tienen ciclos de instalación diferentes; los paquetes deben registrar las versiones que contienen. | [RELEASING.md](RELEASING.md) |

Para cambiar una decisión, registra su nueva fecha, razón y referencia, y actualiza las reglas o guías afectadas. Conserva el historial en Git; no marques una propuesta como decisión aceptada.
