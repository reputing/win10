param([string]$OutputFile = (Join-Path $PSScriptRoot 'BluehourDesktop.exe'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    throw 'The 64-bit .NET Framework compiler is required.'
}
$files = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Xml.dll',
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')",
    "/resource:$(Join-Path $PSScriptRoot 'coast.jpg'),Bluehour.Coast",
    "/resource:$(Join-Path $PSScriptRoot 'Regular.ttf'),Bluehour.Regular",
    "/resource:$(Join-Path $PSScriptRoot 'Bold.ttf'),Bluehour.Bold",
    "/resource:$(Join-Path $PSScriptRoot 'Display.ttf'),Bluehour.Display",
    "/resource:$(Join-Path $PSScriptRoot 'FONT-LICENSE.txt'),Bluehour.FontLicense",
    "/out:$OutputFile"
)
& (Join-Path $framework 'csc.exe') @compilerArgs @files
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }


