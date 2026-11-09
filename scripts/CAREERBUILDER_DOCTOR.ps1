# WICareerBuilder isolated project health doctor; no source or personal-data writes.
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$checks=New-Object System.Collections.Generic.List[object]
function Check([string]$name,[bool]$pass,[string]$info){$checks.Add([pscustomobject]@{Name=$name;Result=$(if($pass){'PASS'}else{'FAIL'});Info=$info})}
Check 'Solution' (Test-Path -LiteralPath (Join-Path $root 'WICareerBuilder.sln') -PathType Leaf) 'WICareerBuilder.sln'
Check 'Sources' (Test-Path -LiteralPath (Join-Path $root 'src') -PathType Container) 'src directory'
Check 'Tests' (Test-Path -LiteralPath (Join-Path $root 'tests') -PathType Container) 'tests directory'
Check 'Gitignore' (Test-Path -LiteralPath (Join-Path $root '.gitignore') -PathType Leaf) '.gitignore'
Check 'Engineering state' (Test-Path -LiteralPath (Join-Path $root 'engineering/project_state.json') -PathType Leaf) 'project_state.json'
$globalPath=Join-Path $root 'global.json'
$validGlobal=$false
if(Test-Path -LiteralPath $globalPath -PathType Leaf){
    try {$json=Get-Content -LiteralPath $globalPath -Raw | ConvertFrom-Json; $validGlobal=($json.test.runner -eq 'Microsoft.Testing.Platform')}catch{}
}
Check '.NET 10 MTP configuration' $validGlobal 'global.json test.runner'
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue
Check '.NET installed' ($null -ne $dotnet) 'dotnet CLI'
if($dotnet){$v=((& dotnet --version)|Out-String).Trim();Check '.NET 10 SDK' ($v -match '^10\.') ('Installed: '+$v)}
foreach($f in @('engineering/project_state.json','engineering/evidence_manifest.json','engineering/git_state.json')){
    $ok=$false
    try {$null=Get-Content -LiteralPath (Join-Path $root $f) -Raw | ConvertFrom-Json;$ok=$true}catch{}
    Check ('Valid JSON: '+$f) $ok $f
}
foreach($c in $checks){Write-Host ('{0,-38} {1,-6} {2}' -f $c.Name,$c.Result,$c.Info)}
$fail=@($checks|Where-Object {$_.Result -eq 'FAIL'}).Count
Write-Host ('DOCTOR: '+$(if($fail -eq 0){'PASS'}else{'FAIL'}))
Write-Host 'Note: Doctor checks prerequisites. It does NOT prove publication/ownership or execute tests.'
exit $(if($fail -eq 0){0}else{1})