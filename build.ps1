$ErrorActionPreference = 'Stop'
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$winmd = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata\*\Windows.winmd" | Where-Object { $_.Directory.Name -match '^\d+\.' } | Sort-Object { [version]$_.Directory.Name } -Descending | Select-Object -First 1
if (!$winmd) { throw 'Windows 10/11 SDK metadata not found.' }
$refs = 'System.dll','System.ServiceProcess.dll','Microsoft.CSharp.dll','System.Core.dll','System.IO.Compression.dll','System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Runtime.Extensions.dll','System.ObjectModel.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Windows.Forms.dll' | ForEach-Object { "/r:$fx\$_" }
$files = (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs').FullName
Add-Type -AssemblyName System.IO.Compression
$bundle = Join-Path ([IO.Path]::GetTempPath()) (([Guid]::NewGuid().ToString('N'))+'.zip')
try {
    $stream = [IO.File]::Create($bundle)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($rel in 'edge-pair.exe','engine/lan-mouse.exe','engine/lan-mouse-engine-source.zip','LICENSE-Lan-Mouse.txt','发现/LICENSE-Go.txt','发现/LICENSE-TOML.txt','首次连接.md') {
                $file = Get-Item -LiteralPath (Join-Path "$PSScriptRoot/桌面键鼠互通" $rel)
                $name = $file.Name
                if ($file.Extension -eq '.exe') { $name = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()+'/'+$name }
                $entry = $zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
                $target = $entry.Open()
                try { $inputFile=$file.OpenRead(); try {$inputFile.CopyTo($target)} finally {$inputFile.Dispose()} } finally {$target.Dispose()}
            }
        } finally {$zip.Dispose()}
    } finally {$stream.Dispose()}
    & "$fx\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/win32icon:$PSScriptRoot/Branding/app.ico" "/resource:$PSScriptRoot/Branding/app.png,brand.png" "/resource:$bundle,runtime.zip" "/out:$PSScriptRoot\雄兴屏拓.exe" "/r:$($winmd.FullName)" @refs @files
if ($LASTEXITCODE) { exit $LASTEXITCODE }

} finally { [IO.File]::Delete($bundle) }
