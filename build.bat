@echo off
setlocal
title Compilando Aviso de REINOS...

echo ========================================================
echo   Compilador de Aviso de REINOS (WoW Retail Monitor)
echo ========================================================
echo.

:: Cerrar proceso en ejecucion si existe para permitir sobreescritura
taskkill /f /im RagnarosGadget.exe >nul 2>&1

set CSC=
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set CSC="%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
) else if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set CSC="%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if "%CSC%"=="" (
    echo [ERROR] No se encontro Microsoft .NET Framework 4.0/4.8 en este sistema.
    echo Asegurate de tener .NET Framework instalado.
    pause
    exit /b 1
)

echo Usando compilador: %CSC%
echo Compilando RagnarosGadget.cs...
echo.

%CSC% /target:winexe /optimize+ /out:RagnarosGadget.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll RagnarosGadget.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================================
    echo  [EXITO] Compilacion completada: RagnarosGadget.exe
    echo ========================================================
) else (
    echo.
    echo ========================================================
    echo  [ERROR] Hubo un error durante la compilacion.
    echo ========================================================
)

echo.
pause
