@echo off
setlocal DisableDelayedExpansion
rem Argumentos: %1 = copia dos fontes, %2 = pasta de saida, %3 = nome.
rem Tambem disponiveis: AC_SOURCE, AC_OUTPUT, AC_NAME.
rem Substitua a copia abaixo pelo comando de build da sua aplicacao.
copy "%AC_SOURCE%\Default.aspx" "%AC_OUTPUT%\Default.aspx" >nul
if errorlevel 1 exit /b 1
echo Compilacao personalizada concluida.
exit /b 0
