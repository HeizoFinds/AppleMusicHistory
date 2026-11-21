param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$gac = Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL'
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/UnionMetadata'
$sdk = Get-ChildItem -LiteralPath $kits -Directory | Where-Object { $_.Name -match '^10\.' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Windows.winmd')) } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows 10/11 SDK metadata (Windows.winmd) is required to build.' }

$references = foreach ($name in 'System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.ObjectModel','System.Threading.Tasks','System.Collections') {
    $assembly = Get-ChildItem -LiteralPath (Join-Path $gac $name) -Recurse -Filter ($name + '.dll') | Select-Object -First 1
    '/reference:' + $assembly.FullName
}
$references += '/reference:' + (Join-Path $sdk.FullName 'Windows.winmd')
$references += '/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')
$references += '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll'
$source = Join-Path $PSScriptRoot 'src'
$files = @(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
$compiler = Join-Path $framework 'csc.exe'
if ($Test) {
    $testExe = Join-Path ([IO.Path]::GetTempPath()) ('AppleMusicHistoryTest-' + [guid]::NewGuid().ToString('N') + '.exe')
    try {
        & $compiler /nologo /codepage:65001 /target:exe /main:AppleMusicHistory.HistoryTests "/out:$testExe" @references @files (Join-Path $PSScriptRoot 'tests/HistoryTests.cs')
        if ($LASTEXITCODE) { throw 'Test compilation failed.' }
        & $testExe
        if ($LASTEXITCODE) { throw 'Playback tests failed.' }
    } finally { if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe } }
} else {
    $output = Join-Path $PSScriptRoot 'AppleMusicHistory.exe'
    & $compiler /nologo /codepage:65001 /optimize+ /target:winexe "/win32icon:$source/History.ico" "/win32manifest:$source/app.manifest" "/out:$output" @references @files
    if ($LASTEXITCODE) { throw 'Compilation failed.' }
    Get-Item -LiteralPath $output | Select-Object FullName,Length
}
