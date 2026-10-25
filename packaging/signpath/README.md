# Preparación local de SignPath

La decisión es utilizar SignPath Foundation más adelante. La solicitud y la firma están deshabilitadas. No hay cuenta, token, GitHub App ni workflow de firma configurado por estos archivos.

- `preparation.json`: decisión, campos futuros vacíos, metadatos esperados y lista de binarios propios.
- `app-artifact.xml`: ZIP de entrada con solo los seis PE propios en su raíz; nombres explícitos y restricciones de producto, compañía y versión de archivo. El parámetro `version` es `VERSION + ".0"` (por ejemplo, `0.10.0.0`). No se restringe `ProductVersion` a ese mismo valor porque .NET incorpora el commit en la versión informativa.
- `installer-artifact.xml`: ZIP de entrada con un único EXE `HABITAT-<version>-x64-setup.exe`. Firma su exterior; no supone que SignPath pueda extraer o firmar el desinstalador embebido de Inno.
- `application-draft.md`: contenido por revisar cuando se retome la candidatura.

Las plantillas XML se comprueban localmente por estructura y alcance. Su esquema y su configuración efectiva deberán validarse con SignPath al disponer de una cuenta autorizada. No son prueba de admisión ni configuraciones activas. No se incluye una copia ejecutable de la acción de envío: incorporarla ahora permitiría activar una solicitud antes de completar el empaquetado.

Los runtimes de Microsoft y demás dependencias conservan sus firmas y licencias. No usar patrones como `**/*.dll` ni firmar el payload completo bajo la identidad de HABITAT. Los assets de Chrome y el VSIX se distribuyen por sus propios mecanismos. Antes de cerrar la integración del ZIP hay que decidir y verificar la firma de los scripts de instalación PowerShell/VBScript; las plantillas actuales solo cubren los PE indicados.

La secuencia completa, el desinstalador, las verificaciones después de la firma, las políticas y los trabajos pendientes están en [docs/SIGNING.md](../../docs/SIGNING.md). El script `scripts/prepare-signing-dossier.ps1` genera únicamente un informe local bajo `artifacts` y no modifica sus entradas.
