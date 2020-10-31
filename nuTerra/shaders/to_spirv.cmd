@echo off
glslangValidator.exe -G -I. PostProcessing/FXAA.vert -o PostProcessing/FXAA.vert.spv
glslangValidator.exe -G -I. PostProcessing/FXAA.frag -o PostProcessing/FXAA.frag.spv
pause
