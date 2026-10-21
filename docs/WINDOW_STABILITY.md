# Diagnóstico de estabilidad de ventana — 2026-10-21

Windows registró en la instalación 0.13.1 cierres nativos en `Microsoft.UI.Windowing.dll` el 20 y 21 de octubre y un bloqueo `AppHangB1` el día 21. El proceso posterior respondía durante la inspección. No se obtuvo una pila del bloqueo, por lo que no se atribuye automáticamente a la misma causa.

Los volcados de los cierres contienen `0xe0464645`, con HRESULT `0x80070057`, al propagar un error desde `AppWindow.Changed`. Una prueba WinUI con almacenamiento aislado reprodujo el mismo cierre en el primer ciclo de minimizar/restaurar. El registro temporal capturó `RasterizationScale=1`, `RightInset=-14`, `TitleBar.Height=32` y `ArgumentException` en `GridLength` dentro de `MainWindow.UpdateCaptionLayout`.

La corrección limita el margen a valores no negativos, valida la escala, evita cambios de tamaño redundantes y agrupa los ajustes en el dispatcher después de la notificación nativa. La actualización de estado tampoco vuelve a escribir el mismo título en cada tick.

Para verificar la regresión desde una sesión con el SDK .NET disponible:

```powershell
scripts/check-tasks-ui.ps1 -WindowMenuOnly -OutputDirectory artifacts/window-stability-checks
```

La prueba usa datos aislados, ejecuta 30 ciclos de minimizar/restaurar, activación y cambio de tamaño, comprueba la zona del menú, deja funcionar las actualizaciones periódicas en reposo y abre/cierra Preferencias en inglés y español. No utiliza la lista de tareas real, capturas de pantalla ni inyección de ratón.

La versión corregida pasó esa prueba y la suite completa de controles WinUI. Después de aplicarla a la instalación, se enviaron 60 consultas `WM_NULL` durante 30,5 segundos: todas respondieron y la latencia máxima fue de 8 ms. Esta observación breve no descarta otros bloqueos espontáneos.

Las suites de lógica y las comprobaciones de distribución se ejecutaron con datos sintéticos. La verificación general se detuvo en `tests/package.checks.ps1` porque la instalación ZIP de prueba rechaza los enlaces de carpetas de la ruta de OneDrive; esa comprobación de instalación no quedó validada.

## Ventana que parece congelada al perder el foco

En una captura posterior de la instancia real (10:35 del 21 de octubre, hora de Madrid), la ventana respondió a `WM_NULL` en 9,8 ms y su hilo principal estaba en `GetMessageW`, dentro del bucle normal de WinUI. Su estilo extendido era `0x00080120`: los bits `WS_EX_LAYERED` y `WS_EX_TRANSPARENT` del modo de paso al escritorio seguían activos mientras otra aplicación tenía el foco.

La activación por el canal interno restauró el estilo a `0x00000100` y puso la misma ventana en primer plano, sin reiniciar el proceso. El usuario confirmó que la barra de tareas y Alt+Tab también recuperaban la interacción. Ese síntoma corresponde al comportamiento de paso al escritorio; no se observó un bloqueo del hilo de interfaz en la captura.

La prueba nativa cubre ahora además la recuperación mediante `WM_SETFOCUS` y activación explícita, aunque no llegue otra notificación `WM_ACTIVATEAPP`. La ruta de foco fallaba antes del refuerzo y pasa después. La desactivación de una ventana individual sigue sin activar el paso al escritorio, para conservar la interacción con los diálogos propios.

El usuario confirmó después el recorrido exacto: pulsar en el escritorio y después en HABITAT dejaba la ventana sin responder al ratón, y Alt+Tab recuperaba siempre la entrada. En 0.14.2 se retira de la ventana principal la integración con `InactiveWindowPassthrough`. Windows vuelve a gestionar la activación normal con el ratón; HABITAT ya no introduce los estilos de paso de clics al perder el foco. Las comprobaciones WinUI verifican que `WS_EX_TRANSPARENT` permanece desactivado durante los cambios de aplicación.
