# Builds dist\ClassLock-Teacher.exe: the teacher panel as one file.
# Uses the C# compiler that ships with Windows; nothing to install.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'ClassLock-Teacher.exe'
$html = Join-Path $root 'ClassLock-Teacher.html'

& $csc /nologo /optimize+ /warnaserror+ /target:winexe "/out:$exe" `
    /r:System.Windows.Forms.dll "/resource:$html,ClassLock-Teacher.html" (Join-Path $root 'TeacherPanel.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# Check: the EXE writes the page, and the page is the same as the source.
$p = Start-Process $exe -ArgumentList '/extract' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'EXE test failed' }
$out = Join-Path $env:LOCALAPPDATA 'ClassLockTeacher\ClassLock-Teacher.html'
if ((Get-FileHash $out).Hash -ne (Get-FileHash $html).Hash) { throw 'Page inside the EXE differs from the source' }
Write-Host "Built $exe"
