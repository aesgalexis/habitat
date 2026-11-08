# HABITAT

[English](README.md) | **Español**

**Un espacio de trabajo nativo de Windows para escribir y conectar tus herramientas.**

Conversa, busca, traduce, prepara correos y ejecuta comandos desde un mismo editor. HABITAT conecta tus sesiones del navegador y tus aplicaciones de escritorio, con tareas, contactos y widgets junto a tu trabajo.

[Descargar para Windows](https://github.com/aesgalexis/habitat/releases/latest) · [Primeros pasos](docs/GETTING_STARTED.md) · [Documentación](docs/README.md) · [Changelog](CHANGELOG.md)

Windows 10 / 11 · x64 · C# · WinUI 3 · .NET 10 · [Licencia MIT](LICENSE)

## Instalación

Requiere **Windows 10 versión 2004 o posterior, o Windows 11, en x64**. Los paquetes incluyen .NET y Windows App SDK; no necesitas SDK ni permisos de administrador.

1. Descarga el instalador EXE para Windows x64 y su archivo SHA-256 desde [GitHub Releases](https://github.com/aesgalexis/habitat/releases/latest). Comprueba el hash y ejecuta el instalador desde tu sesión normal de Windows.
2. Abre HABITAT y sigue **Set up HABITAT…**. El instalador añade un acceso al menú Inicio; la guía de conexión sigue disponible en el menú de la aplicación.
3. Para las integraciones del navegador, usa **Chrome 120 o posterior** e instala **HABITAT · browser bridge** desde la guía. Después, elige **Enable connection**. También puedes cargar la extensión incluida mediante **Open extension folder** y **Load unpacked** en Chrome.
4. Para ChatGPT, inicia sesión en Chrome y abre la conversación que quieras usar. Configura las demás integraciones cuando las necesites.

Gmail requiere tu propio [cliente OAuth de Google para escritorio](docs/GMAIL_SETUP.md). VS Code necesita la [extensión HABITAT Terminal](vscode-extension/README.md) incluida. Codex y Phone Link deben estar instalados y configurados por separado.

Si prefieres el ZIP, extrae todos sus archivos y ejecuta `Install-HABITAT.cmd`. Mantén el mismo formato EXE o ZIP al actualizar. Las actualizaciones desde la aplicación descargan y verifican el paquete correspondiente, conservan borradores y datos, y vuelven a abrir HABITAT. Consulta [actualizaciones](docs/UPDATES.md).

La firma de código está [aplazada](docs/SIGNING.md); el instalador actual no está firmado. La [guía de primeros pasos](docs/GETTING_STARTED.md) amplía la instalación y las conexiones.

## Destinos

Elige un destino a la derecha, escribe y pulsa **Enter** para ejecutar su acción; **Shift+Enter** inserta un salto de línea. Todos empiezan con el editor centrado y muestran los resultados después de actuar. La rueda del ratón sobre el selector cambia de destino.

| Destino | Qué hace |
| --- | --- |
| **OpenAI (ChatGPT)** | Envía a la conversación vinculada de ChatGPT en Chrome y muestra sus respuestas de texto en HABITAT. |
| **Google** | Muestra resultados de búsqueda dentro de HABITAT; los enlaces se abren en Chrome. |
| **YouTube** | Busca vídeos y canales con miniaturas; los abre en Chrome para reproducirlos. |
| **Notepad** | Inserta el texto en una pestaña nueva de Bloc de notas, sin guardar archivos. |
| **Translate** | Traduce mediante Google Translate al inglés, griego o español. |
| **Gmail** | Guarda un borrador de correo sin enviarlo en la cuenta conectada. |
| **Google Maps** | Abre búsquedas de lugares o rutas en Chrome. |
| **Codex** | Trabaja con un chat de Codex vinculado de forma persistente y muestra sus respuestas de texto. |
| **VS Code** | Ejecuta comandos en terminales integrados nuevos del proyecto local elegido y permite seguir su salida. |
| **Terminal** | Permite revisar y confirmar un comando antes de ejecutarlo en una nueva ventana de PowerShell. |
| **Run** | Abre programas, comandos, archivos y carpetas de Windows. |

El texto sin enviar puede acompañarte al cambiar de destino. Tras ejecutar una acción, cada destino mantiene su propio estado y protege las ediciones posteriores. ChatGPT sigue el chat activo de Chrome entre solicitudes; una respuesta en curso permanece en su conversación original. Codex conserva el chat vinculado a HABITAT al navegar y reiniciar.

## Adapta tu espacio

- **Contactos.** Crea una persona con teléfonos, correos y perfil de Messenger, y selecciónala como destino. Los canales compatibles usan borradores de Gmail, Messenger o Phone Link. La agenda empieza vacía; **Connect Google Drive** permite sincronizar tus contactos de HABITAT entre equipos. [Configuración y límites de los canales](docs/CONTACTS.md).
- **Barra lateral.** Ten a mano tareas locales, correos sin leer de Gmail y el tiempo. Añade el widget opcional Markets para cotizaciones, gráficas y titulares; elige y ordena los widgets en Preferences. [Barra lateral](docs/SIDEBAR.md) · [Markets](docs/MARKETS.md).
- **Google Drive.** Elige qué contactos y tareas sincronizar en **Preferences → Google Drive**. Ambos usan la carpeta **HABITAT** en Mi unidad. La sincronización de tareas empieza desactivada. [Sincronización de tareas](docs/TASKS_DRIVE_SYNC.md).
- **Mensajes programados para Codex.** Programa un mensaje para un chat nuevo o elige cualquier chat local disponible, incluidos los iniciados en Codex. HABITAT debe permanecer abierto con la sesión de Windows iniciada. [Programación](docs/CODEX_SCHEDULING.md).
- **Destinos personales y apariencia.** Ordena los destinos, añade aplicaciones de Windows y elige los temas Day, Night o Hybrid, con fondos incluidos o imágenes propias. [Fondos](docs/WALLPAPERS.md).
- **Ctrl+H.** Abre o enfoca HABITAT y lleva el texto seleccionado al editor sin enviarlo. [Atajo global](docs/GLOBAL_SHORTCUT.md).

La interfaz del producto está en inglés. Tus mensajes, tareas, datos de contacto y demás contenido conservan su idioma original.

## Privacidad y límites

HABITAT utiliza tu sesión de ChatGPT en Chrome, sin clave de la API de OpenAI. La extensión se comunica con la aplicación mediante una conexión local autenticada. Gmail y la sincronización opcional con Google Drive usan autorizaciones de Google independientes.

Las tareas, preferencias y contactos se guardan en tu cuenta de Windows. El Administrador de credenciales de Windows protege las autorizaciones de Google. La sincronización opcional sube a tu cuenta de Google los contactos y las tareas que actives. Consulta la [privacidad del escritorio](docs/PRIVACY.md) y de la [extensión](docs/EXTENSION_PRIVACY.md).

Las integraciones dependen de las aplicaciones conectadas y pueden necesitar mantenimiento cuando estas cambien. Consulta el contenido enriquecido, las herramientas y las solicitudes de aprobación en la aplicación original. La disponibilidad y el transporte de Phone Link dependen del teléfono vinculado. Si un envío queda sin confirmar, revisa la conversación original antes de reintentarlo.

## Compilar y verificar

Para desarrollar necesitas **Windows x64**, el SDK fijado en [global.json](global.json) y Node.js. Ejecuta estos comandos desde la raíz del repositorio.

```powershell
dotnet restore Habitat.sln -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-winui.ps1
```

La compilación se prepara y verifica dentro de `artifacts`. Añade `-Install` para activarla en tu copia de desarrollo, ejecutándolo desde tu sesión normal de Windows; después usa `HABITAT.vbs` para abrirla.

Para comprobar la documentación o ejecutar la verificación completa:

```powershell
node scripts/check-docs.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

La verificación completa ejecuta comprobaciones automatizadas y una compilación Release, con datos de prueba aislados bajo `artifacts`. Consulta [cómo colaborar](CONTRIBUTING.md), las [comprobaciones](docs/CHECKS.md) y la [arquitectura](docs/ARCHITECTURE.md).

## Documentación y soporte

- [Guía de uso](docs/USER_GUIDE.md) y [resolución de problemas](docs/TROUBLESHOOTING.md).
- [Estado del proyecto](docs/PROJECT_STATUS.md), [changelog](CHANGELOG.md) y [proceso de publicación](docs/RELEASING.md).
- [Índice de documentación](docs/README.md) con las guías de instalación, integraciones, privacidad y desarrollo.
- [Comunicar un problema](https://github.com/aesgalexis/habitat/issues) indicando la versión instalada y el estado de las conexiones relevantes.

El código fuente declara la versión **0.20.2**. Los paquetes publicados y sus versiones se consultan en [GitHub Releases](https://github.com/aesgalexis/habitat/releases).

## Licencia

[MIT](LICENSE) · © 2026 aesgalexis
