setlocal
cd /d %~dp0
call setkey.cmd
cd src
del *nupkg /s
dotnet pack
dir *nupkg /s/b > temp.txt
set /p NUGET_FILE= < temp.txt
del temp.txt
set SERVER=%1
if "%SERVER%" == "" set SERVER=https://api.nuget.org/v3/index.json
@dotnet nuget push %NUGET_FILE% -k %NUGET_ORG_KEY% -s %SERVER%
