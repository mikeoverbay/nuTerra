@echo off
glslangValidator.exe -G -I. PostProcessing/FXAA.vert -o PostProcessing/FXAA.vert.nonopt.spv
glslangValidator.exe -G -I. PostProcessing/FXAA.frag -o PostProcessing/FXAA.frag.nonopt.spv

REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.vert.nonopt.spv -o PostProcessing/FXAA.vert.nonopt.txt
REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.frag.nonopt.spv -o PostProcessing/FXAA.frag.nonopt.txt

spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 PostProcessing/FXAA.vert.nonopt.spv -o PostProcessing/FXAA.vert.spv
spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 PostProcessing/FXAA.frag.nonopt.spv -o PostProcessing/FXAA.frag.spv

REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.vert.spv -o PostProcessing/FXAA.vert.txt
REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.frag.spv -o PostProcessing/FXAA.frag.txt

del PostProcessing\FXAA.vert.nonopt.spv
del PostProcessing\FXAA.frag.nonopt.spv

pause
