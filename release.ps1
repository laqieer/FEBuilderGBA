$folder = "r\"
if (!(Test-Path $folder)) {
    New-Item -ItemType Directory -Path $folder
    New-Item -ItemType Directory -Path $folder\config
    New-Item -ItemType Directory -Path $folder\config\etc
    New-Item -ItemType Directory -Path $folder\config\log
}
Copy-Item -Force "FEBuilderGBA\bin\Release\FEBuilderGBA.exe" $folder
Copy-Item -Force "FEBuilderGBA\bin\Release\*.dll" $folder
Copy-Item -Force "FEBuilderGBA\bin\Release\*.json" $folder
Copy-Item -Force "FEBuilderGBA\bin\Debug\7-zip32.dll" $folder
Copy-Item -Force -Recurse "FEBuilderGBA\bin\Debug\config\*" $folder\config -Exclude @("etc","log")
Copy-Item -Force *.md $folder
