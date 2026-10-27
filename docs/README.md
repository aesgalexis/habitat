# Documentación de HABITAT

Entrada para usuarios y colaboradores. La documentación combina inglés y español; los textos de interfaz del producto deben permanecer en inglés según [AGENTS.md](../AGENTS.md).

## Instalar y usar

| Necesidad | Referencia |
| --- | --- |
| Instalar y conectar por primera vez | [GETTING_STARTED.md](GETTING_STARTED.md) |
| Usar los destinos y widgets presentes | [USER_GUIDE.md](USER_GUIDE.md) |
| Resolver un problema o preparar una incidencia | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |
| Actualizar y conocer qué se conserva | [UPDATES.md](UPDATES.md) |
| Autorizar Gmail | [GMAIL_SETUP.md](GMAIL_SETUP.md) |
| Agenda unificada, fuentes opcionales y fusiones | [CONTACTS.md](CONTACTS.md) |
| Captura de selección con Ctrl+H | [GLOBAL_SHORTCUT.md](GLOBAL_SHORTCUT.md) |
| Contactos de Facebook, integración en desarrollo en esta copia | [FACEBOOK.md](FACEBOOK.md) |
| Datos del escritorio y de la extensión | [PRIVACY.md](PRIVACY.md), [EXTENSION_PRIVACY.md](EXTENSION_PRIVACY.md) |

## Desarrollar y distribuir

| Necesidad | Referencia |
| --- | --- |
| Preparar entorno, colaborar y entender los hooks | [CONTRIBUTING.md](../CONTRIBUTING.md) |
| Ejecutar comprobaciones sin datos reales | [CHECKS.md](CHECKS.md) |
| Ver qué está respaldado por esta copia | [PROJECT_STATUS.md](PROJECT_STATUS.md) |
| Entender los módulos | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Contrato del puente local | [BRIDGE_PROTOCOL.md](BRIDGE_PROTOCOL.md) |
| Política de idioma | [LOCALIZATION.md](LOCALIZATION.md) |
| Decisiones y sus límites | [DECISIONS.md](DECISIONS.md) |
| Preparar y publicar versiones | [RELEASING.md](RELEASING.md), [changelog](../CHANGELOG.md) |
| Distribución externa | [WINGET.md](WINGET.md), [CHROME_WEB_STORE.md](CHROME_WEB_STORE.md) |
| Firma aplazada y preparación local | [SIGNING.md](SIGNING.md), [política propuesta](CODE_SIGNING_POLICY.md) |
| Extensión de VS Code | [README de la extensión](../vscode-extension/README.md) |

## Funciones recuperadas e informes históricos

[PROJECT_STATUS.md](PROJECT_STATUS.md) identifica las funciones presentes. La [recuperación del 25 de octubre](RECOVERY_2026-10-25.md) vuelve a integrar [Markets](MARKETS.md), [sidebar](SIDEBAR.md), [programación de Codex](CODEX_SCHEDULING.md), [fondos](WALLPAPERS.md) y [colección ilustrada](WALLPAPER_ART.md), conservando Facebook.

Las auditorías del [15 de octubre](ARCHITECTURE_AUDIT.md) y del [18 de octubre](ARCHITECTURE_AUDIT_2026-10-18.md), y el [diagnóstico de ventana](WINDOW_STABILITY.md), registran estados y validaciones de fechas concretas. No certifican el árbol de trabajo actual. [PRODUCT_STATE_AND_VISION.md](PRODUCT_STATE_AND_VISION.md) conserva filosofía, feedback e ideas; el estado técnico se mantiene por separado.

## Mantener este índice

Al añadir o retirar una guía, actualiza sus enlaces y ejecuta `node scripts/check-docs.cjs` desde la raíz. Cada tema debe tener una referencia principal; [AUTO_UPDATES.md](AUTO_UPDATES.md) se conserva únicamente como enlace compatible hacia `UPDATES.md`.
