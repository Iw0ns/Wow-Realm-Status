@echo off
title Subir Wow Realm Status a GitHub
echo ========================================================
echo    Subiendo Wow Realm Status a GitHub
echo    Destino: https://github.com/Iw0ns/Wow-Realm-Status
echo ========================================================
echo.
cd /d "%~dp0"
git push -u origin main
echo.
if %errorlevel% equ 0 (
    echo ========================================================
    echo  [EXITO] El proyecto se subio correctamente!
    echo  Visitalo en: https://github.com/Iw0ns/Wow-Realm-Status
    echo ========================================================
) else (
    echo ========================================================
    echo  [AVISO] Hubo un error al subir.
    echo  Si GitHub te pide autenticarte, autoriza en tu navegador.
    echo ========================================================
)
echo.
pause
