# HuBattery

HuBattery muestra un símbolo de mouse y el porcentaje de batería junto al reloj de Windows. Funciona como un ejecutable portátil y consulta el nivel cada 30 segundos.

## Uso

1. Descarga `HuBattery.exe` desde [Releases](https://github.com/ZielCL/HuBattery/releases).
2. Abre el ejecutable y mueve el mouse si estaba en reposo.
3. Haz clic izquierdo en el indicador para actualizar. Abre el menú con clic derecho para elegir un mouse si hay varios con lectura, iniciar con Windows o salir.

El indicador se coloca junto a la bandeja en la barra de tareas principal horizontal. Si la barra no permite ese formato, aparece un icono normal en la zona de notificación, que Windows puede dejar bajo la flecha de iconos ocultos.

## Compatibilidad

| Conexión | Lectura de batería | Estado |
| --- | --- | --- |
| Receptor USB Razer DeathAdder V3 HyperSpeed `1532:00C5` | Consulta HID de Razer | Verificado en Windows con hardware |
| Otros Razer USB del [catálogo](src/RazerBattery.cs) | Consulta HID por modelo | Implementado según referencias, pendiente de pruebas en cada modelo |
| Mouse Bluetooth | Lee el porcentaje si Windows lo publica para el dispositivo | Pendiente de pruebas con hardware Bluetooth |
| Mouse con campo HID estándar Battery Strength | Lee el campo si existe en un reporte de características de la colección del mouse | Pendiente de pruebas con hardware compatible |

Un receptor de 2,4 GHz puede usar un protocolo propio aunque Windows reconozca el mouse. Si la barra muestra `—`, HuBattery todavía no recibió un porcentaje válido. Logitech HID++, SteelSeries, Corsair y otros receptores propietarios necesitan validación o un lector específico.

## Cómo funciona

- [MouseBattery.cs](src/MouseBattery.cs) reúne las lecturas, elimina duplicados del mismo contenedor de Windows y conserva la selección actual durante la sesión.
- [DeviceNodes.cs](src/DeviceNodes.cs) identifica los dispositivos y busca el nivel que publica Windows para ratones Bluetooth. [Guía de batería Bluetooth de Microsoft](https://learn.microsoft.com/windows-hardware/design/accessory-guidelines/bluetooth-accessory-guidelines/bluetooth-accessory-guidelines-battery-profiles-services).
- [HidBattery.cs](src/HidBattery.cs) consulta solo el campo HID Battery Strength cuando el propio descriptor del mouse lo declara. [API de valores HID de Microsoft](https://learn.microsoft.com/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getspecificvaluecaps).
- [RazerBattery.cs](src/RazerBattery.cs) envía únicamente la consulta de nivel de batería a los identificadores USB del catálogo. [Referencia de protocolo de OpenRazer](https://github.com/openrazer/openrazer/blob/master/driver/razermouse_driver.c) y [referencia de identificadores USB](https://github.com/xzeldon/razer-battery-report#supported-mice).

## Compilación

En Windows 10 de 64 bits, versión 1607 o posterior, o Windows 11, ejecuta `build.cmd`. El resultado queda en `dist\HuBattery.exe`. La compilación usa el compilador de .NET Framework incluido en Windows.

## Reportar un mouse

Abre un [issue](https://github.com/ZielCL/HuBattery/issues/new) con marca y modelo, tipo de conexión, versión de Windows y el resultado que viste. Para receptores USB, incluye `VID_xxxx&PID_xxxx` de **Administrador de dispositivos → Propiedades → Detalles → Id. de hardware**. Para Bluetooth, indica si Configuración de Windows ya muestra el porcentaje.

## Avance

- **v0.1.0**. Indicador portátil de un solo mouse, selección cuando hay varias lecturas y detección por Bluetooth de Windows, HID estándar y catálogo USB Razer.
