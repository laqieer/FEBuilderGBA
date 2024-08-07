$folder = "r\"
if (!(Test-Path $folder)) {
    New-Item -ItemType Directory -Path $folder
}
Copy-Item -Force "FEBuilderGBA\bin\Release\FEBuilderGBA.exe" $folder
Copy-Item -Force "FEBuilderGBA\bin\Debug\7-zip32.dll" $folder
Copy-Item -Force -Recurse "FEBuilderGBA\bin\Debug\config" $folder
Copy-Item -Force *.md $folder
