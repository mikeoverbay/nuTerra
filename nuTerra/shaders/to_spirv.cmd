@echo off
glslangValidator.exe -G -I. PostProcessing/FXAA.vert -o PostProcessing/FXAA.vert.nonopt.spv
glslangValidator.exe -G -I. PostProcessing/FXAA.frag -o PostProcessing/FXAA.frag.nonopt.spv

glslangValidator.exe -G -I. Utility/TextRender.frag -o Utility/TextRender.frag.nonopt.spv
glslangValidator.exe -G -I. Utility/TextRender.vert -o Utility/TextRender.vert.nonopt.spv

REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.vert.nonopt.spv -o PostProcessing/FXAA.vert.nonopt.txt
REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.frag.nonopt.spv -o PostProcessing/FXAA.frag.nonopt.txt

spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 PostProcessing/FXAA.vert.nonopt.spv -o PostProcessing/FXAA.vert.spv
spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 PostProcessing/FXAA.frag.nonopt.spv -o PostProcessing/FXAA.frag.spv

spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 Utility/TextRender.vert.nonopt.spv -o Utility/TextRender.vert.spv
spirv-tools-x64\spirv-opt.exe -O --target-env=opengl4.5 Utility/TextRender.frag.nonopt.spv -o Utility/TextRender.frag.spv

REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.vert.spv -o PostProcessing/FXAA.vert.txt
REM spirv-tools-x64\spirv-dis.exe PostProcessing/FXAA.frag.spv -o PostProcessing/FXAA.frag.txt

del PostProcessing\FXAA.vert.nonopt.spv
del PostProcessing\FXAA.frag.nonopt.spv

del Utility\TextRender.vert.nonopt.spv
del Utility\TextRender.frag.nonopt.spv

pause
