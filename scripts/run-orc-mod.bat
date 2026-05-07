@echo off
REM Executa Hero-Engine com o mod test-orc-mod (herda de alisyum)
echo === Hero-Engine - Config: test-orc-mod ===
dotnet run --project Core -- --config test-orc-mod
pause
