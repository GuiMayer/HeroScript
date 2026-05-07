@echo off
echo Starting HeroScript Math API...
echo.
echo The API will be available at:
echo   - Swagger UI: http://localhost:5260
echo   - API Base: http://localhost:5260/api
echo.
echo Available endpoints:
echo   GET  /api/formula - List all formulas
echo   GET  /api/formula/{name} - Get formula details
echo   POST /api/formula/evaluate - Evaluate a formula
echo   POST /api/formula/reload - Reload formula cache
echo   GET  /api/math/expression/operations - List operations
echo   POST /api/math/expression/evaluate - Evaluate expression
echo.
echo Press Ctrl+C to stop the server
echo.
cd /d "%~dp0"
dotnet run --project API/API.csproj
