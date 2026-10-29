param([string]$EditorPath='C:/Program Files/Unity/Hub/Editor/2022.3.2f1/Editor')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$editorData=Join-Path $EditorPath 'Data'
$compiler=Join-Path $editorData 'DotNetSdkRoslyn/csc.dll'
$runtime=Join-Path $editorData 'NetCoreRuntime/dotnet.exe'
$uiAssembly=Join-Path $projectRoot 'Library/ScriptAssemblies/UnityEngine.UI.dll'
foreach($dependency in @($compiler,$runtime,$uiAssembly)) {
    if(!(Test-Path -LiteralPath $dependency)){throw "Required Unity reference is missing: $dependency. Import the project with the matching Editor first."}
}
$outputDirectory=Join-Path $projectRoot '.utmp/unity-reference-checks'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$references=@(Get-ChildItem (Join-Path $editorData 'NetStandard/ref/2.1.0') -Filter '*.dll')
$references+=@(Get-ChildItem (Join-Path $editorData 'Managed') -Filter 'UnityEngine*.dll' -Recurse)
$runtimeSources=@(Get-ChildItem (Join-Path $projectRoot 'Assets/Scripts') -Filter '*.cs' -Recurse)
foreach($target in @('Player','Editor')) {
    $sources=$runtimeSources
    $targetReferences=$references
    $defines='UNITY_2022_3;UNITY_2022_3_OR_NEWER;UNITY_5_3_OR_NEWER;UNITY_STANDALONE;UNITY_STANDALONE_WIN'
    if($target -eq 'Editor') {
        $sources+=@(Get-ChildItem (Join-Path $projectRoot 'Assets/Editor') -Filter '*.cs' -Recurse)
        $targetReferences+=@(Get-ChildItem (Join-Path $editorData 'Managed') -Filter 'UnityEditor*.dll' -Recurse)
        $defines+=';UNITY_EDITOR;UNITY_EDITOR_WIN'
    }
    $arguments=@('-nostdlib+','-target:library','-langversion:9.0','-nullable:disable',
        ('-out:"'+(Join-Path $outputDirectory ($target+'.dll'))+'"'),('-define:'+$defines))
    $arguments+=@($targetReferences | ForEach-Object { '-r:"'+$_.FullName+'"' })+@('-r:"'+$uiAssembly+'"')
    $arguments+=@($sources | ForEach-Object { '"'+$_.FullName+'"' })
    $responsePath=Join-Path $outputDirectory ($target+'.rsp')
    [IO.File]::WriteAllLines($responsePath,$arguments)
    & $runtime $compiler '-noconfig' ('@'+$responsePath)
    if($LASTEXITCODE -ne 0){throw "$target Unity-reference compilation failed ($LASTEXITCODE)."}
    Write-Output "$target source compiled against installed Unity 2022.3.2f1 assemblies."
}
Write-Output 'Reference compilation does not run the Unity importer, scenes, or Play Mode checks.'
