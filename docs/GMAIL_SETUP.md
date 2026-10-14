# Gmail en HABITAT

Gmail está integrado en la aplicación WinUI 3 mediante la API oficial de Google y OAuth de escritorio. Cada instalación necesita configurar su propio cliente y autorizar la cuenta que quiera consultar. No se incluyen credenciales ni cuentas preconfiguradas en el repositorio.

## Sesión persistente

HABITAT carga automáticamente la cuenta cifrada al abrirse y renueva el acceso con el refresh token cuando hace falta. Cerrar la ventana, reiniciar Windows o actualizar HABITAT conserva la sesión. Volver a importar el mismo cliente OAuth también conserva la cuenta. Los errores de red o configuración no eliminan el consentimiento guardado; una autorización caducada o revocada sí requiere volver a entrar. **Disconnect** desconecta expresamente la cuenta.

La persistencia se comprueba con un archivo DPAPI de prueba: guardar la cuenta, crear una nueva instancia, renovar un access token caducado sin navegador y crear una tercera instancia que reutiliza el token renovado. Esto es independiente de la caducidad de siete días que Google impone a los refresh tokens emitidos en modo **Testing**.

## Coste

El uso estándar de Gmail API es gratuito dentro de las cuotas de Google. Esta integración consulta directamente Google desde el equipo, sin servidor alojado, intermediarios de pago ni suscripciones. Mantén este proyecto sin cuenta de facturación y no contrates ampliaciones de cuota. Google anuncia posibles cargos por superar las cuotas; esta configuración no habilita facturación.

Consulta las [cuotas y precios oficiales](https://developers.google.com/workspace/gmail/api/reference/quota). La rotación del widget reutiliza los datos cargados; no genera peticiones extra.

## Cuando quieras conectarlo

1. En [Google Cloud Console](https://console.cloud.google.com/), crea o selecciona un proyecto dedicado a HABITAT, sin facturación vinculada.
2. Activa **Gmail API** en **APIs & Services → Library**.
3. Configura **Google Auth Platform**: nombre de la aplicación, correo de asistencia y contacto, y público externo para una cuenta Gmail personal. En **Testing**, pulsa **Agregar usuarios**, añade la dirección de Gmail que vas a conectar y guarda. Comprueba que aparece en la tabla de usuarios de prueba después de cerrar el formulario. Usa únicamente el permiso `https://www.googleapis.com/auth/gmail.readonly`.
4. Crea un cliente OAuth de tipo **Desktop app** y descarga su JSON. No elijas **Web application** ni una cuenta de servicio.
5. Pulsa el sobre de HABITAT o **••• → Gmail… → Setup… → Import client JSON…** y selecciona el archivo.
6. Pulsa **Sign in with Google**. Google abrirá el navegador del sistema para que tú elijas la cuenta y concedas permiso. HABITAT no recoge tu contraseña. Si aparece una advertencia de aplicación no verificada, revisa que el proyecto y el cliente sean los tuyos; no concedas acceso a un cliente desconocido.
7. Regresa a HABITAT. Verás la cuenta, el contador de no leídos y su lista. Puedes elegir **Inbox unread** o **All unread**. Usa **Load more** para otras páginas.

No hace falta desplegar HABITAT en Google Cloud: sigue siendo una aplicación de escritorio. Google Cloud proporciona la configuración de OAuth y Gmail API.

### Error 403: access_denied

Si Google indica que la aplicación está en pruebas y solo admite testers aprobados, comprueba que estás usando el mismo proyecto del cliente importado y que la cuenta seleccionada aparece en su tabla de usuarios de prueba. Escribir la dirección en el formulario sin completar el guardado no basta. Si la consola falla al cargar o guardar, vuelve a abrirla en tu navegador habitual y comprueba la tabla antes de reintentar el inicio de sesión.

## Comportamiento y privacidad

- El widget superior muestra el icono y contador junto a tres líneas: remitente, asunto y fragmento. Rota cada ocho segundos entre los no leídos cargados en la lista. Se pausa al pasar el ratón, abrir el lector o durante una carga. Conserva el correo mostrado al actualizar si sigue en la lista. En ventanas estrechas se reduce al icono y contador para dejar espacio al tiempo.
- La rotación usa los fragmentos ya cargados en memoria; no descarga cuerpos completos ni realiza consultas adicionales a Google. El texto largo se recorta con puntos suspensivos.
- El widget tiene dos botones independientes: icono y contador abren el lector de HABITAT; remitente, asunto y fragmento abren Gmail en el navegador del sistema y en la cuenta conectada. Se busca el correo por su cabecera Message-ID mediante `rfc822msgid`; si no existe esa cabecera se usa el enlace a su conversación. Cada zona tiene su propio resaltado. La rotación también se pausa al enfocar el mensaje con el teclado.
- Ninguna de las dos zonas muestra tooltips al pasar el ratón.
- Leer dentro de HABITAT no marca el correo como leído. Abrirlo en Gmail sigue el comportamiento normal de Gmail. Si no hay un mensaje mostrado, el bloque de texto abre el lector local.
- Solo lectura: no envía, elimina, archiva ni marca mensajes como leídos.
- La lista se actualiza cada 60 segundos mientras HABITAT está abierto. Cada página muestra hasta 25 mensajes; el contador de **All unread** es una estimación de Google y se señala con `≈`.
- El cuerpo se obtiene al seleccionar un mensaje. Se presenta exclusivamente como texto, sin ejecutar HTML ni cargar imágenes remotas. No se descargan adjuntos.
- Ningún correo se envía a ChatGPT, ni se trata como una instrucción para la aplicación.
- El cliente OAuth y los tokens se guardan cifrados mediante Windows DPAPI en `%LOCALAPPDATA%\HABITAT\gmail-account.bin`, vinculados al usuario de Windows. Los correos solo se conservan en memoria durante esa ejecución.
- **Disconnect** elimina los tokens del almacenamiento local y limpia la vista. Mantiene el cliente OAuth para reconectar. Para revocar el consentimiento en Google, usa la sección de conexiones de tu cuenta de Google.
- No guardes el JSON de Google en Git. Los nombres habituales están ignorados, pero cualquier archivo de credenciales debe mantenerse fuera del repositorio.
- En proyectos externos en **Testing**, Google puede caducar el refresh token a los siete días. Si ocurre, vuelve a iniciar sesión. Distribuir la aplicación a otros usuarios puede requerir verificación de Google por el permiso Gmail de alcance restringido.

Documentación oficial: [OAuth para aplicaciones de escritorio](https://developers.google.com/identity/protocols/oauth2/native-app), [permisos Gmail](https://developers.google.com/workspace/gmail/api/auth/scopes), [caducidad de tokens](https://developers.google.com/identity/protocols/oauth2#expiration).

## Comprobaciones sin cuenta real

```powershell
dotnet run --project tests\Habitat.Gmail.Checks
```

Las pruebas usan respuestas HTTP sintéticas y un callback local. No abren Google, no consultan ningún buzón ni envían mensajes a ChatGPT.

## Configuración desde terminal

La utilidad `tools/Habitat.Gmail.Setup` utiliza los mismos servicios OAuth y almacenamiento DPAPI que HABITAT. Para importar un cliente e iniciar sesión sin automatizar la interfaz de escritorio:

```powershell
dotnet run --project tools\Habitat.Gmail.Setup -- connect C:\ruta\client_secret.json
```

Para verificar una cuenta ya conectada y mostrar solo el contador de no leídos:

```powershell
dotnet run --project tools\Habitat.Gmail.Setup -- check
```

La utilidad no imprime tokens ni contenido de correos. Si HABITAT estaba abierto durante la configuración desde terminal, reinícialo para cargar la cuenta cifrada.
