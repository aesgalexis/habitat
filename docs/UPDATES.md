# Actualizaciones desde HABITAT

HABITAT consulta la última publicación estable de `aesgalexis/habitat` en GitHub al arrancar y cada seis horas mientras permanece abierto. Si falla la consulta, reintenta después de quince minutos sin mostrar avisos. No descarga ni instala nada hasta que el usuario lo solicita.

Cuando hay una versión superior con el paquete adecuado y su SHA-256 ya subidos, se abre automáticamente un aviso modal en el centro de la ventana, con el botón **Update** centrado. Bloquea la interacción con el resto de HABITAT: editor, navegación entre destinos y captura con el atajo global. Si hay otro diálogo abierto, espera a que se cierre. No desplaza el editor ni reserva espacio en el sidebar. No aparece para la versión actual, versiones anteriores, borradores, prereleases o publicaciones incompletas.

**Update** confirma la instalación y el reinicio; **Later** cierra el aviso sin descargar ni modificar el borrador. La misma versión no vuelve a interrumpir durante esa sesión; **HABITAT menu → Update…** permite retomarla o reintentar tras un error. Los borradores se guardan antes del reinicio y se restauran al abrir; las tareas guardadas, preferencias y credenciales se conservan. La descarga mantiene el bloqueo modal y ofrece **Cancel**. Antes del reinicio se espera a que termine el trabajo en curso en HABITAT, incluidos los envíos programados y manuales de Codex, hasta quince minutos. Las aplicaciones externas conservan sus propios procesos y documentos.

## Publicación y formatos

Se mantiene el flujo de [RELEASING.md](RELEASING.md): integrar y subir el código verificado a `main`, incrementar `VERSION`, documentar los cambios y subir una etiqueta `vMAJOR.MINOR.PATCH`. El workflow publica EXE, ZIP y hashes. Un push o checkpoint sin una nueva publicación no genera una actualización para los usuarios. No se necesita un servidor adicional ni WinGet.

- Una instalación EXE descarga el EXE y ejecuta Inno Setup en la misma carpeta, sin reiniciar Windows. Su recuperación sigue siendo la del instalador; no incorpora la recuperación transaccional del ZIP.
- Una instalación ZIP utiliza el manifiesto de archivos, prepara otra carpeta y cambia el puntero de la versión activa. Mantiene la versión anterior y la restaura si la aplicación nueva no llega a iniciar su puente local.
- La instalación de desarrollo con `HABITAT.vbs` y una publicación activa en `artifacts` utiliza el mismo mecanismo ZIP. Actualiza binarios e integraciones en una carpeta de publicación nueva, sin sobrescribir código fuente ni hacer `git pull`. Las integraciones incluidas se buscan primero junto a los binarios activos. Las extensiones de Chrome y VS Code siguen su ciclo propio de instalación/actualización.

Las versiones antiguas que todavía no incluyen el actualizador necesitan instalar manualmente una primera versión que sí lo contenga. Un ejecutable suelto, una compilación no activada, una sesión de pruebas aislada o una ejecución con identidad de paquete/sandbox no ofrece instalación automática.

## Integridad, ejecución y diagnóstico

Se aceptan exclusivamente assets con el nombre y ruta esperados de este repositorio, a través de HTTPS. Los redireccionamientos se limitan a los hosts de descarga de GitHub. Antes de la instalación se vuelve a consultar la publicación, se verifica tamaño y SHA-256 y, para ZIP, también versión, runtime y el manifiesto completo. La extracción rechaza rutas que salgan del directorio, duplicados y enlaces. Un SHA-256 detecta corrupción; no equivale a una firma de editor. La firma de código continúa aplazada según [SIGNING.md](SIGNING.md).

El ayudante local comprueba otra vez el hash y la identidad del proceso original. Confirma que está preparado antes de que HABITAT se cierre, espera su salida y detiene únicamente el residente del atajo de esa instalación. Todo se ejecuta en la sesión normal de Windows. Una descarga fallida deja HABITAT abierto; un error posterior intenta reabrir la versión anterior y permite reintentar desde **Update**.

Los paquetes y diagnósticos quedan en `%LOCALAPPDATA%\HABITAT\updates\update-<id>`. `last-result.json` registra el resultado de la última instalación y `operations.log` registra fallos de comprobación o descarga. No se envían conversaciones, tareas, credenciales ni telemetría al desarrollador. GitHub recibe las consultas de publicación y, al actualizar, las descargas con sus metadatos normales de red.

## Verificación

`Habitat.Core.Checks` utiliza HTTP simulado para probar versiones, publicaciones incompletas, redirecciones, cancelación, hashes, metadatos y extracción. `tests/update.checks.ps1` prueba activación ZIP, integraciones, conservación del código, recuperación tras fallo de arranque y argumentos del EXE con archivos sintéticos en `artifacts`. `scripts/check-tasks-ui.ps1 -UpdatesOnly` verifica el aviso automático, el botón centrado con ambos temas y estados del sidebar, foco modal, bloqueo de navegación y captura, ausencia de diálogos duplicados, aplazamiento, cancelación y reintento. No instala publicaciones remotas ni accede a la lista real de tareas.
