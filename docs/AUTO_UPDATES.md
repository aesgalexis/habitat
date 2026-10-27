# Actualizaciones desde HABITAT

Esta guía conserva detalles del mecanismo histórico `AppUpdateInstall/UpdateHelper`.
El flujo vigente de `DesktopUpdateService/ReleaseUpdateClient/install-release-update`
y los diagnósticos se describen en [UPDATES.md](UPDATES.md). Para la instalación de
desarrollo 0.16.1 afectada, usa la [reproducción y recuperación específica](UPDATE_MIGRATION_0_16_1.md).

Desde 0.15.0, pulsar el icono **Actualizar HABITAT / Update HABITAT** descarga e instala la nueva versión y reabre la aplicación. Las versiones anteriores solo abren GitHub: necesitan una instalación manual de 0.15.0 para incorporar esta función.

La detección consulta la última publicación estable al arrancar y cada seis horas. Los fallos reintentan tras una hora sin borrar un aviso conocido. Pulsar el icono refresca los metadatos aunque no haya vencido ese intervalo. No se descargan paquetes hasta pulsarlo.

## Descarga y cierre

- Se conserva el formato de la instalación: EXE con desinstalador registrado o ZIP con lanzador y selector de publicación activos. Las copias de desarrollo ofrecen el enlace oficial para instalación manual.
- Las URLs se construyen exclusivamente para los assets oficiales de `aesgalexis/habitat`. Se exigen paquete y SHA-256 subidos, nombres correspondientes a la versión y un tamaño máximo de 512 MiB. El checksum tiene un límite de 4 KiB y debe identificar el archivo exacto.
- La descarga muestra el porcentaje como texto accesible en el idioma elegido. Es cancelable y se verifica antes de renombrar el archivo temporal. Un fallo o cancelación elimina solo la carpeta nueva de esa descarga y conserva la app abierta.
- Antes de cerrar se espera a los envíos y acciones en curso. Se guardan el destino y los borradores compartidos y locales, sin traducir su contenido. El cierre drena las operaciones normales y conserva las tareas, preferencias y credenciales en sus rutas habituales.

## Instalación y recuperación

Un proceso oculto de Windows PowerShell, con argumentos estructurados y sin perfil, comprueba el paquete y la identidad del proceso original. La app espera su confirmación de preparación antes de cerrarse; si no llega en treinta segundos permanece abierta. El auxiliar espera hasta dos minutos al cierre, detiene su componente de atajo y vuelve a verificar el SHA-256 antes de ejecutar el paquete.

El EXE usa el instalador silencioso sobre la misma ruta, espera su finalización y comprueba la versión antes de reabrir. Conserva las limitaciones de recuperación de Inno Setup; no proporciona la restauración transaccional del ZIP. El ZIP rechaza entradas que escapen del destino, enlaces y tamaños excesivos, comprueba `release.json` y reutiliza el instalador distribuido, con sus manifiestos, publicación anterior y recuperación del arranque.

Ante un fallo del auxiliar se conserva `install.log` en la carpeta de descarga, se registra el resultado en el perfil y se intenta reabrir la instalación existente. Al arrancar se recuperan los borradores una sola vez y se muestra el aviso de reintento si hubo error. Una interrupción de Windows durante la instalación puede requerir volver a ejecutar el instalador oficial.

## Comprobaciones

`Habitat.Core.Checks` verifica detección, refresco manual, formatos, límites, integridad, cancelación, limpieza y recuperación de borradores. En Windows compila un ejecutable sintético para comprobar el auxiliar real: espera del proceso padre, instalación simulada correcta o fallida, reapertura y ZIP válido o rechazado por recorrido de rutas o versión incorrecta. Todo queda bajo `artifacts`, sin usar la instalación ni datos del usuario.

`--updates-ui-only` comprueba inglés y español, ambos temas, posición del icono, errores de checksum y lanzamiento, cancelación, clics duplicados y entrega al auxiliar con borradores recuperables. Usa HTTP simulado y un lanzador que nunca ejecuta un instalador. La publicación de GitHub verifica además los paquetes reales y sus ciclos aislados de instalación.
