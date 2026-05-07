@echo off
REM Executa todos os testes do Hero-Engine
echo === Hero-Engine - Executando Testes ===
dotnet run --project Core -- --test
pause
