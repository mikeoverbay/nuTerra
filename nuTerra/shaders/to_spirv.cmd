@echo off

cd "%~dp0"

if not exist ..\bin\Release exit

set shaders=PostProcessing\FXAA.vert PostProcessing\FXAA.frag ^
            Utility\TextRender.frag Utility\TextRender.vert

for %%q in (%shaders%) do (
    glslangValidator.exe -G -I. %%q -o %%q.nonopt.spv

    REM spirv-tools-x64\spirv-dis.exe %%q.nonopt.spv -o %%q.nonopt.txt

    spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 %%q.nonopt.spv -o %%q.spv

    REM spirv-tools-x64\spirv-dis.exe %%q.spv -o %%q.txt

    del %%q.nonopt.spv
    move %%q.spv ..\bin\Release\shaders\%%q.spv
)
