# 🛡️ Aviso de REINOS (WoW Retail Realm Monitor)

[![Build](https://github.com/Iw0ns/Wow-Realm-Status/actions/workflows/build.yml/badge.svg)](https://github.com/Iw0ns/Wow-Realm-Status/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](#)
[![WoW: Retail](https://img.shields.io/badge/World%20of%20Warcraft-Retail-red.svg)](https://worldofwarcraft.blizzard.com/)

**Aviso de REINOS** (Ragnaros Gadget) es una aplicación ligera para Windows diseñada para monitorear en tiempo real el estado de los servidores de **World of Warcraft Retail** (especialmente útil los martes de mantenimiento), alertándote de forma instantánea en cuanto el reino vuelve a estar disponible sin necesidad de recargar la web de Blizzard manualmente.

---

## ✨ Características Principales

- 🟢 **Indicador Visual en Tiempo Real:**
  - 🟢 **VERDE:** Servidor en línea y disponible (`Online`).
  - 🔴 **ROJO:** Servidor inactivo, caído o en mantenimiento (`Offline` / `Maintenance`).
  - Icono dinámico en la **Bandeja del Sistema (System Tray)** junto al reloj de Windows.
- ⏱️ **Comprobación Automática Cada 2 Minutos:**
  - Monitorea el estado cada 120 segundos en segundo plano de forma ultra-ligera.
  - Cuenta regresiva en vivo en la ventana de estado (*ej. "Próxima comprobación: en 1m 35s"*).
- ⏳ **Predicción Cronológica de Mantenimiento:**
  - Guarda un historial inteligente de la duración de mantenimientos pasados.
  - Durante el mantenimiento de los martes, predice automáticamente el tiempo restante aproximado (*ej. "Normalmente este mantenimiento termina en ~32 min."*).
- 🔔 **Avisos Periódicos Cada 15 Minutos:**
  - Si el servidor continúa en mantenimiento, te envía un recordatorio cada 15 minutos con el tiempo restante estimado.
  - En cuanto el servidor sube a **VERDE**, te alerta inmediatamente para ingresar a jugar.
- 🛡️ **Notificaciones 100% Nativas de Windows:**
  - Utiliza el sistema nativo de notificaciones de la barra de tareas de Windows (`ShowBalloonTip`).
  - **Sin barras invisibles:** No genera ventanas transparentes que interfieran con los clics de las bolsas o barras de acción dentro de WoW.
  - **Cero cuelgues:** No interrumpe las cadenas de intercambio de DirectX (Direct3D 11/12).
- 🔊 **Alertas Sonoras:**
  - Emite campanillas audibles del sistema (`SystemSounds.Asterisk` / `Exclamation`) para que te enteres incluso si estás en otra aplicación o lejos del monitor.
- 🚀 **Conexión Directa a la API GraphQL de Blizzard:**
  - Sin navegadores en segundo plano (cero consumo de Edge o Chromium).
  - Respuestas en menos de **300 ms** con **0% de uso de CPU**.
- ⚙️ **Configuración Flexible:**
  - Compatible con cualquier reino de WoW Retail (Ragnaros, Quel'Thalas, Drakkari, Illidan, Area 52, etc.).
  - Soporta regiones: `us` (América y Oceanía), `eu` (Europa), `kr` (Corea), `tw` (Taiwán).
  - Opción de inicio automático con Windows.

---

## 🖥️ Requisitos

- **Sistema Operativo:** Windows 10 o Windows 11 (64-bit o 32-bit).
- **.NET Framework:** 4.5 o superior (incluido por defecto en Windows).
- **Conexión a Internet:** Para consultar la API pública de Blizzard.

---

## 🛠️ Cómo Compilar

No necesitas instalar herramientas pesadas. Windows ya incluye el compilador nativo de C# (`csc.exe`).

### Opción 1: Compilación Rápida (1 Clic)
Simplemente haz doble clic en el archivo:
```bat
build.bat
```
Esto generará inmediatamente el ejecutable optimizado `RagnarosGadget.exe`.

### Opción 2: Compilar desde Línea de Comandos (PowerShell / CMD)
```cmd
%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:RagnarosGadget.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll RagnarosGadget.cs
```

### Opción 3: Visual Studio
Abre la carpeta o el archivo `AvisoDeReinos.csproj` en **Visual Studio 2019 / 2022** y presiona `Ctrl + Shift + B` (Compilar).

---

## 🚀 Uso

1. Ejecuta `RagnarosGadget.exe`.
2. La aplicación aparecerá en la **bandeja del sistema** (junto al reloj de Windows):
   - **Clic izquierdo:** Abre la ventana con los detalles de población, estado y cuenta regresiva.
   - **Clic derecho:** Despliega el menú con opciones rápidas:
     - *Comprobar ahora*
     - *Probar aviso de servidor ACTIVO (Verde)*
     - *Probar aviso estimado de mantenimiento (~32 min)*
     - *Configuración...*
     - *Iniciar con Windows*
     - *Salir*

---

## 📁 Estructura del Proyecto

```text
├── .github/
│   └── workflows/
│       └── build.yml          # Automatización de compilación con GitHub Actions
├── AvisoDeReinos.csproj       # Archivo de proyecto para Visual Studio / MSBuild
├── build.bat                  # Script de compilación rápida en un clic
├── LICENSE                    # Licencia MIT
├── RagnarosGadget.cs          # Código fuente principal de la aplicación
├── README.md                  # Documentación del proyecto
└── .gitignore                 # Filtro de archivos compilados y temporales
```

---

## 📄 Licencia

Este proyecto está bajo la Licencia [MIT](LICENSE). Siéntete libre de usarlo, modificarlo y compartirlo.

---

*World of Warcraft y Blizzard Entertainment son marcas comerciales o marcas registradas de Blizzard Entertainment, Inc. Este proyecto es una herramienta independiente creada por la comunidad y no está afiliada ni respaldada por Blizzard Entertainment.*
