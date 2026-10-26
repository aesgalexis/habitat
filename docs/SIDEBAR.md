# Barra lateral personalizable

**Preferences → Sidebar / Preferencias → Barra lateral** permite añadir, quitar y ordenar Gmail, Tareas y Mercados. El tiempo permanece siempre visible y fijo arriba. El menú contextual de la barra ofrece **Customize sidebar… / Personalizar barra lateral…** para abrir esa sección directamente.

La lista utiliza botones de subir, bajar y quitar, con nombres accesibles y tooltips en el idioma elegido. El selector de elementos disponibles solo contiene los que aún no están en la barra. Al cerrar Preferencias se guardan y aplican los cambios sin reiniciar. Retirar un widget conserva las tareas, la cuenta de Gmail y los favoritos de Mercados. Si se retira Tareas con su panel abierto, el panel se cierra; el menú principal sigue permitiendo abrir la lista.

Las instalaciones nuevas muestran Tiempo, Gmail y Tareas. Mercados está desactivado por defecto. Los perfiles anteriores que todavía no tienen `SidebarWidgets` conservan los cuatro widgets en su orden previo. Una selección vacía explícita significa mostrar únicamente Tiempo.

En la misma sección **Sidebar**, **Contact card size** permite elegir **Small cards (default)** o **Large cards** para las tarjetas de [contactos de Facebook](FACEBOOK.md). El cambio se guarda y aplica al cerrar Preferencias; el selector no ocupa espacio en la lista de contactos.

`SidebarCatalog` define los identificadores estables `weather`, `gmail`, `tasks` y `markets`. `Preferences.SidebarWidgets` guarda únicamente los opcionales, en orden. La normalización descarta identificadores desconocidos y duplicados; Tiempo se antepone siempre. `MainPage.Sidebar` recompone las filas y el orden de navegación sin huecos y mantiene la misma disposición tanto plegada como desplegada. El panel de tareas calcula su posición a partir de las filas activas. Retirar Mercados impide iniciar nuevas consultas periódicas; una consulta ya iniciada puede terminar y los favoritos permanecen guardados.

`SidebarChecks` en `Habitat.Core.Checks` verifica valores iniciales, migración, persistencia, selección vacía, normalización y etiquetas. `--preferences-ui-only` verifica los controles WinUI en inglés y español, edición y guardado, posición fija del tiempo, orden y anchuras en ambos estados, conservación de borradores, posición del panel de tareas y consultas de Mercados con respuestas sintéticas. Las pruebas usan exclusivamente almacenamiento aislado bajo `artifacts`.
