@echo off
set "NUGET_SOURCE=http://nuget.wnConsign.com/nuget"
set "API_KEY=wnNug3tK3y!"
dotnet nuget push "bin\Release\*.nupkg" --source "%NUGET_SOURCE%" --api-key "%API_KEY%"
REM Also try artifacts/packages for 0.3.1 layout:
dotnet nuget push "artifacts\packages\Zplr.Renderer.0.3.1.nupkg" --source "%NUGET_SOURCE%" --api-key "%API_KEY%" --skip-duplicate
