param([string]$Output='KazeSubtitleEditor.exe')
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Push-Location $PSScriptRoot
try {
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $chd=Join-Path $PSScriptRoot '..\chdman.exe'
 & $compiler /nologo /utf8output /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$Output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:assets\manifest.json,manifest.json /resource:assets\glyphs.gz,glyphs.gz "/resource:$chd,chdman.exe" /resource:..\LICENSES.txt,LICENSES.txt Core.cs Editor.cs Sector.cs ..\LicenseDialog.cs
 if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
} finally {Pop-Location}
