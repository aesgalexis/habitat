# Firma de código: decisión y preparación

## Decisión del propietario

**19 de octubre de 2026: elegir SignPath Foundation y aplazar la solicitud.** Antes hay que mejorar el empaquetado del producto. Esta decisión sustituye cualquier intención anterior de empezar ya el alta.

No enviar solicitudes de admisión ni de firma, crear cuentas, instalar aplicaciones de GitHub, aceptar condiciones, añadir tokens o activar workflows de SignPath hasta que el propietario retome expresamente el proceso. No contactar con la fundación en nombre del proyecto. La preparación actual es local y no cambia las publicaciones existentes.

## Preparación incluida

- [Política de firma](CODE_SIGNING_POLICY.md): borrador para el repositorio, roles propuestos y límites. No afirma que SignPath nos haya aceptado.
- [Notas de privacidad del escritorio](PRIVACY.md), complementarias a la política de la extensión. Describen también las conexiones automáticas actuales.
- [Borrador de candidatura](../packaging/signpath/application-draft.md), sin datos personales nuevos ni formulario enviado.
- [Configuración de preparación](../packaging/signpath/preparation.json): firma y envíos deshabilitados, identificadores de cuenta vacíos y lista explícita de seis binarios propios.
- Plantillas XML para [binarios de la app](../packaging/signpath/app-artifact.xml) e [instalador](../packaging/signpath/installer-artifact.xml). Son borradores locales; no están registrados ni validados por SignPath.
- [Auditoría local](../scripts/prepare-signing-dossier.ps1): verifica el manifiesto de un payload dentro de `artifacts`, inventaría hashes/metadatos, separa los PE de terceros y genera pendientes. No copia datos de usuario, ejecuta binarios, firma, valida certificados en red ni hace llamadas a SignPath.
- Metadatos de las próximas compilaciones unificados como producto `HABITAT` y compañía `aesgalexis`, incluida la información de producto del instalador. El nombre legal del firmante lo determinaría el certificado aprobado; estos metadatos no son un certificado.

## Trabajo de empaquetado antes de solicitar

| Prioridad | Trabajo | Criterio de cierre |
| --- | --- | --- |
| P0 | Un único payload verificado para EXE y ZIP | Compilar los binarios propios una vez y usar exactamente esos bytes en ambos formatos. Ahora los scripts publican la aplicación por separado. |
| P0 | Firma por etapas y manifiestos finales | Aplicar las firmas a los binarios propios antes de generar `build-manifest.json`; crear el ZIP desde ese payload. Firmar el EXE antes de calcular su SHA-256 y los manifiestos WinGet. Verificar el resultado y probar los paquetes finales. |
| P0 | Desinstalador de Inno Setup | Diseñar y ensayar `SignedUninstaller`/`SignedUninstallerDir` o la integración `SignTool`. Firmar únicamente el EXE exterior no firma el desinstalador embebido. El proceso futuro debe mantener procedencia verificable también para ese artefacto. |
| P1 | Alcance de la instalación ZIP | Decidir y verificar la firma de los scripts propios PowerShell/VBScript y sus módulos, con nombres explícitos y configuración separada. Firmar los PE dentro de un ZIP no acredita automáticamente todos sus scripts ni convierte al ZIP en un instalador Authenticode. |
| P0 | Privacidad y opciones de red al instalar | Mostrar información de privacidad y una elección clara para el tiempo automático. El interruptor de ubicación actual no apaga las consultas meteorológicas. Revisar los consentimientos y la desconexión de Gmail/extensión y los cambios de inicio automático. |
| P1 | Inventario de terceros y licencias | Recoger dependencias directas/transitivas, versiones, origen, licencias y avisos redistribuibles del payload final. Revisar qué componentes encajan en las condiciones de la fundación. El listado de archivos no equivale a un SBOM ni a una revisión legal de licencias. |
| P1 | Procedencia y aprobación de publicaciones | Runners alojados por GitHub para todos los trabajos previos a la firma; commit y etiqueta revisados; referencias de actions fijadas a commits auditados; protección de `main` y entorno de aprobación. Confirmar MFA y responsables, sin afirmar que ya estén configurados. |
| P1 | Instalación real desde cero y actualización anterior | Probar arranque en un Windows limpio, Chrome ausente/presente, instalación desde tienda o descomprimida, consentimiento, cierre de procesos, actualización desde la versión anterior y desinstalación conservando datos. Los checks actuales cubren instalación aislada y reinstalación de la misma versión, no todo ese recorrido. |
| P2 | Presentación del producto | Revisar textos, documentación, identidad visual y ayuda de primera instalación. Publicar una versión más pulida antes de presentar la candidatura; recopilar evidencias reales de mantenimiento y uso sin inventar reputación. |

No basta con tener el repositorio público y una licencia MIT: la aceptación del proyecto y la reputación exigida dependen de SignPath Foundation.

## Generar un informe sin activar nada

Usa un payload ya generado o extraído en `artifacts`, nunca `%LOCALAPPDATA%` ni la lista real de tareas:

```powershell
./scripts/prepare-signing-dossier.ps1 `
  -PayloadDirectory artifacts/public/0.10.0/zip/artifacts/staged/app `
  -InstallerPath artifacts/public/0.10.0/HABITAT-0.10.0-x64-setup.exe `
  -ReleaseMetadataPath artifacts/public/0.10.0/zip/release.json
```

El script crea `readiness.json` y `readiness.md` en un directorio nuevo de `artifacts/signpath-preparation`. Los paquetes ya publicados de 0.10.0 conservarán los metadatos anteriores; el informe debe indicar sus diferencias. Un informe generado correctamente no significa que estemos listos para solicitar: `readyToApply`, `submissionAllowed` y `signingEnabled` permanecen en `false`.

## Secuencia futura, solo cuando se retome

1. Cerrar el trabajo de empaquetado de la tabla, revisar las políticas actuales y aprobar la candidatura con el propietario.
2. Confirmar contacto público, funciones de autor/revisor/aprobador y MFA. Completar la solicitud y las condiciones solo con esa nueva autorización.
3. Si se acepta el proyecto, registrar las configuraciones revisadas de artefactos y la política con aprobación manual. Configurar entonces los identificadores y el token de alcance mínimo como secreto de GitHub, nunca en Git ni en los paquetes.
4. Integrar en el workflow existente el upload de los binarios propios como artefacto de GitHub y la acción oficial de SignPath. Conservar el ID del artefacto y la procedencia del build. Revisar y fijar el commit de las actions; no usar un token para firmar ficheros arbitrarios o builds locales.
5. Descargar y verificar los binarios firmados, el firmante permitido, el timestamp y sus metadatos; reinsertarlos en el payload y regenerar su manifiesto. Resolver la firma del desinstalador durante el empaquetado. Firmar el instalador final y después generar hashes/WinGet y probar todos los formatos finales.
6. Publicar una **versión nueva**. No añadir firmas ni sustituir bytes en los assets o etiquetas de 0.10.0 ya publicados. Actualizar la política con la atribución exigida por SignPath únicamente después de su aceptación.

La firma acredita procedencia y editor; no garantiza que SmartScreen suprima todos los avisos desde la primera descarga.

## Referencias oficiales revisadas

- [Condiciones de SignPath Foundation](https://signpath.org/terms.html).
- [Integración con GitHub y procedencia](https://docs.signpath.io/trusted-build-systems/github).
- [Ejemplos y restricciones de metadatos](https://docs.signpath.io/artifact-configuration/examples).
- [Formatos y firma Authenticode](https://docs.signpath.io/artifact-configuration/reference).
- [Inno Setup: SignedUninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm) y [SignTool](https://jrsoftware.org/ishelp/topic_setup_signtool.htm).
