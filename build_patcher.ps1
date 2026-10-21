param([string]$Output = "KazeRegretDCKRPatcher.exe")
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Push-Location $PSScriptRoot
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 & $compiler /nologo /utf8output /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$Output" /resource:chdman.exe,chdman.exe /resource:disc1.kzrp,disc1.kzrp /resource:disc2.kzrp,disc2.kzrp /resource:LICENSES.txt,LICENSES.txt /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll MainForm.cs Patcher.cs Sector.cs Finder.cs Recipe.cs LicenseDialog.cs
 if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
} finally {Pop-Location}
