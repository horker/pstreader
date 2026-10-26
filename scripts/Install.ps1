& $PSScriptRoot\Build-Module.ps1

Copy-Item -Recurse -Force $PSScriptRoot\..\artifacts\PowerShell\PstReader\ $env:USERPROFILE\Documents\PowerShell\Modules\