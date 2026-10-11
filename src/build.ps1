param([string]$OutputPath, [string]$ContractsDirectory)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$contracts = Join-Path $PSScriptRoot '.build\contracts\ref\netstandard2.0'
if ($ContractsDirectory) { $contracts = $ContractsDirectory }
if (-not (Test-Path $contracts)) {
 $cache = Join-Path $PSScriptRoot '.build'
 New-Item -ItemType Directory -Path $cache -Force | Out-Null
 $archive = Join-Path $cache 'contracts.zip'
 Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.contracts/10.0.26100.4948/microsoft.windows.sdk.contracts.10.0.26100.4948.nupkg' -OutFile $archive
 Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $cache 'contracts') -Force
 $contracts = Join-Path $cache 'contracts\ref\netstandard2.0'
}
$refs = @('System.Runtime.WindowsRuntime.dll','System.Runtime.dll','System.Threading.Tasks.dll','System.Net.Http.dll','System.Web.Extensions.dll','Microsoft.VisualBasic.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\WindowsBase.dll','System.Xaml.dll') | ForEach-Object { Join-Path $framework $_ }
$refs += @(Get-ChildItem $contracts -Filter '*.winmd' | ForEach-Object { $_.FullName })
$refs += @("$framework\WPF\UIAutomationClient.dll", "$framework\WPF\UIAutomationTypes.dll")
$destination = Join-Path $PSScriptRoot '..\FloatingMusic.exe'
if ($OutputPath) { $destination = $OutputPath }
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/out:$destination" "/resource:$PSScriptRoot\Player.xaml,Player.xaml" "/resource:$PSScriptRoot\Queue.xaml,Queue.xaml" ($refs | ForEach-Object { '/r:' + $_ }) "$PSScriptRoot\Player.cs" "$PSScriptRoot\Lyrics.cs" "$PSScriptRoot\LyricCandidates.cs" "$PSScriptRoot\AudioVolume.cs" "$PSScriptRoot\PointerSlider.cs" "$PSScriptRoot\AppleSeek.cs" "$PSScriptRoot\LibraryBridge.cs" "$PSScriptRoot\LibraryPanel.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "Built: $destination"



