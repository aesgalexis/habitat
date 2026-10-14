# HABITAT — WinUI 3

Aplicación de escritorio nativa para Windows que permite escribir en el chat activo de ChatGPT en Chrome y recibir sus respuestas dentro de HABITAT. No crea chats separados, no rota conversaciones ni cambia sus nombres. Usa la sesión real de Chrome, sin consumir la API de OpenAI. Una respuesta en curso termina en su chat original antes de seguir el chat activo.

## Ejecutar

1. Abre `HABITAT.vbs`. Inicia la aplicación sin mostrar una consola.
2. En Chrome, selecciona la pestaña y conversación de ChatGPT que quieras usar.
3. Si aún no está instalada, carga `extension` desde `chrome://extensions` con **Modo desarrollador → Cargar descomprimida**.
4. Espera a que HABITAT muestre **Extensión conectada**.

La extensión se vuelve a emparejar automáticamente cuando HABITAT cambia su clave al reiniciarse. El código manual sigue disponible en el menú de tres puntos como mecanismo de recuperación.

Chrome puede quedarse en segundo plano, minimizado o mostrando otra pestaña. La extensión sigue el chat activo y usa un WebSocket local desde su proceso de fondo (Chrome 120 o posterior), no temporizadores dentro de la página oculta. Las comprobaciones de conexión siguen funcionando mientras ChatGPT responde. La pestaña vinculada se excluye temporalmente del descarte automático de Chrome; su valor anterior se restablece al desconectar HABITAT. No se cambian ajustes globales, perfiles ni cookies.

Si Chrome ya había descartado o congelado completamente esa pestaña, HABITAT lo indica y necesita que la abras una vez para reanudarla; no la recarga por su cuenta ni borra un posible borrador. Cerrar Chrome o suspender Windows también interrumpe la conexión. Al cambiar de chat en Chrome, los próximos mensajes se envían al chat activo.

En el editor, Enter envía el mensaje y Shift+Enter inserta un salto de línea. Las respuestas de texto vuelven a HABITAT y permanecen también en la conversación original de ChatGPT.

El modo **Text-only replies** está activado por defecto. Añade a cada envío una instrucción visible para que ChatGPT responda con texto simple y evite tablas, imágenes, archivos y otros contenidos enriquecidos, salvo que el propio mensaje los solicite expresamente. Se puede desactivar y su valor queda guardado desde **Preferences…**.

El tiempo aparece en la esquina superior derecha, sin marco, con temperatura en °C y previsión de lluvia para las próximas seis horas. **Use current location (GPS / Windows)** está activado por defecto en Preferences: usa el servicio de ubicación de Windows, con GPS cuando está disponible. Solicita el permiso desde HABITAT y actualiza la posición junto al tiempo cada 15 minutos, sin seguimiento continuo. Las coordenadas se redondean a tres decimales y se envían a [Open-Meteo](https://open-meteo.com/en/docs) para obtener el pronóstico; no se guardan en disco ni se envían al chat.

Si Windows no obtiene una posición o deniega el acceso, usa la ciudad guardada (inicialmente `Palma, ES`) e indica **saved city**; el motivo aparece al pasar el ratón. En **Preferences…** se puede desactivar la ubicación automática, cambiar **Fallback city / Weather location**, o abrir **Windows location settings**. En Windows: **Configuración → Privacidad y seguridad → Ubicación → Servicios de ubicación** y el permiso para aplicaciones de escritorio. El porcentaje mostrado es el máximo de las probabilidades horarias de precipitación de las próximas seis horas. Si falla la conexión, lo indica y reintenta automáticamente.

## Gmail

El sobre de la esquina superior izquierda abre el lector de Gmail; también está en **••• → Gmail…**. Muestra los no leídos, permite abrir su texto sin marcarlos como leídos y actualiza la lista cada minuto. No envía correos ni comparte su contenido con ChatGPT. El proyecto **HABITAT Gmail** (`habitat-gmail-20261014`) tiene Gmail API habilitada y la facturación desactivada. El cliente **HABITAT Desktop** ya está creado e importado en este equipo. La cuenta ya está conectada: HABITAT recupera la sesión cifrada al abrirse y renueva el acceso automáticamente. En modo OAuth Testing, Google puede exigir una nueva autorización cada siete días. El uso estándar de la API es gratuito dentro de las cuotas de Google y no requiere ningún intermediario de pago. Las credenciales se cifran para el usuario de Windows y no se guardan en Git.

Las instrucciones completas están en [docs/GMAIL_SETUP.md](docs/GMAIL_SETUP.md). Las comprobaciones sin cuenta real se ejecutan con `dotnet run --project tests\Habitat.Gmail.Checks`.

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

Para comprobar el transporte en segundo plano: `node tests\extension-background.checks.cjs` y `dotnet run --project tests\Habitat.Bridge.Checks`. Usan páginas sintéticas y un puerto local aleatorio, nunca tu conversación real. El endpoint local `/status`, con la clave de vinculación, ofrece diagnóstico de solo lectura: consultarlo no crea un heartbeat ni consume mensajes.

## Seguridad y límites

- El servidor escucha exclusivamente en `127.0.0.1:8765` y exige una clave aleatoria nueva en cada ejecución.
- No publiques el código de vinculación.
- Los mensajes nuevos van al chat activo de Chrome; selecciona la conversación deseada antes de enviar.
- Los selectores de la extensión dependen de la interfaz web de ChatGPT y pueden requerir mantenimiento cuando ésta cambie.
- Widgets, archivos, imágenes y otras interfaces complejas no se reproducen en HABITAT; consúltalos en Chrome.
- No repitas un envío fallido sin revisar antes Chrome: el mensaje podría haber llegado aunque la confirmación no haya vuelto.

## Checkpoints automáticos en GitHub

Los hooks versionados en `.githooks` trabajan sobre la rama `codex/autosave`. Antes de subir, compilan la solución C#, validan la extensión y revisan el diff. El hook `post-commit` envía automáticamente el commit a `origin/codex/autosave`.

Activa los hooks en un clon nuevo con:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-autopush.ps1
```
