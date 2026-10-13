# HABITAT — WinUI 3

Aplicación de escritorio nativa para Windows que permite escribir en una pestaña existente de ChatGPT y recibir sus respuestas dentro de HABITAT. Usa la sesión real de Chrome: no consume la API de OpenAI ni crea otra conversación.

## Ejecutar

1. Abre `HABITAT.vbs`. Inicia la aplicación sin mostrar una consola.
2. En Chrome, abre la conversación de ChatGPT que quieras mantener vinculada.
3. Si aún no está instalada, carga `extension` desde `chrome://extensions` con **Modo desarrollador → Cargar descomprimida**.
4. Espera a que HABITAT muestre **Extensión conectada**.

La extensión se vuelve a emparejar automáticamente cuando HABITAT cambia su clave al reiniciarse. El código manual sigue disponible en el menú de tres puntos como mecanismo de recuperación.

En el editor, Enter envía el mensaje y Shift+Enter inserta un salto de línea. Las respuestas de texto vuelven a HABITAT y permanecen también en la conversación original de ChatGPT.

El modo **Text-only replies** está activado por defecto. Añade a cada envío una instrucción visible para que ChatGPT responda con texto simple y evite tablas, imágenes, archivos y otros contenidos enriquecidos, salvo que el propio mensaje los solicite expresamente. Se puede desactivar y su valor queda guardado desde **Preferences…**.

El tiempo aparece en la esquina superior derecha, sin marco, con temperatura en °C y previsión de lluvia para las próximas seis horas. **Use current location (GPS / Windows)** está activado por defecto en Preferences: usa el servicio de ubicación de Windows, con GPS cuando está disponible. Solicita el permiso desde HABITAT y actualiza la posición junto al tiempo cada 15 minutos, sin seguimiento continuo. Las coordenadas se redondean a tres decimales y se envían a [Open-Meteo](https://open-meteo.com/en/docs) para obtener el pronóstico; no se guardan en disco ni se envían al chat.

Si Windows no obtiene una posición o deniega el acceso, usa la ciudad guardada (inicialmente `Palma, ES`) e indica **saved city**; el motivo aparece al pasar el ratón. En **Preferences…** se puede desactivar la ubicación automática, cambiar **Fallback city / Weather location**, o abrir **Windows location settings**. En Windows: **Configuración → Privacidad y seguridad → Ubicación → Servicios de ubicación** y el permiso para aplicaciones de escritorio. El porcentaje mostrado es el máximo de las probabilidades horarias de precipitación de las próximas seis horas. Si falla la conexión, lo indica y reintenta automáticamente.

## Compilar

Requisitos de desarrollo:

- Windows 10, versión 2004 o posterior.
- SDK de .NET 10.
- Plantillas oficiales `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`.

Genera la versión autocontenida x64 con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1
```

El resultado se guarda en `artifacts\win-x64`. Es una aplicación WinUI 3 sin paquete, por lo que no necesita Modo de desarrollador ni abre una consola. La solución es `Habitat.sln` y el proyecto principal está en `src\Habitat.App`.

Las comprobaciones del pronóstico, sin red ni dependencias de pruebas adicionales, se ejecutan con `dotnet run --project tests\Habitat.Weather.Checks`. Para comprobar la conexión real del servicio de tiempo para Palma: `dotnet run --project tests\Habitat.Weather.Checks -- --live`.

## Seguridad y límites

- El servidor escucha exclusivamente en `127.0.0.1:8765` y exige una clave aleatoria nueva en cada ejecución.
- No publiques el código de vinculación.
- El vínculo queda fijado a la URL de la conversación elegida. Si esa pestaña navega a otra conversación, hay que volver a vincularla.
- Los selectores de la extensión dependen de la interfaz web de ChatGPT y pueden requerir mantenimiento cuando ésta cambie.
- Widgets, archivos, imágenes y otras interfaces complejas no se reproducen en HABITAT; consúltalos en Chrome.
- No repitas un envío fallido sin revisar antes Chrome: el mensaje podría haber llegado aunque la confirmación no haya vuelto.

## Checkpoints automáticos en GitHub

Los hooks versionados en `.githooks` trabajan sobre la rama `codex/autosave`. Antes de subir, compilan la solución C#, validan la extensión y revisan el diff. El hook `post-commit` envía automáticamente el commit a `origin/codex/autosave`.

Activa los hooks en un clon nuevo con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```
