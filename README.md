## NuTerra - A work in progress...

### This is the home of nuTerra where all the work will be done to rewrite Terra using New OpenGL techniques.

### Update log.. 2/10/2020
App is now 100% 4.3+ CORE. All rendering is done using FBOs or VAOs. All transforms happen in code or shader code.</br>
All this is Maxim's work.</br>
Maxim also reworte the shader loader to make it MUCH easier to add a shader!</br>
I added 3D cross hair.</br>
Fixed the Deferred lighting.</br>
Added back in a gPosition FBO render texture.</br>
Fixed bugs and improved code readability.</br>
Added code to the deferred shader to use the GMF flag to stop shading color only objects like the sun Orb, Cross Hair, wire frame and TBN visualizer.</br>

#### Update log.. 2/9/2020
Added frustrum culling for the models. the VAO is rendering fast. Maxim has cleaned up and removed the code and simplified some functions.</br>

#### Update log.. 2/5/2020
All transforms take place in shaders now.</br>
The base_model_holder_ structure is ready to be loaded from the space.bin data. I need to add code to extract the visual information and place it in each component of each model.</br>
Code is looking better and more orginized thanks to Maxim and the work he has done. Also he fixed the AMD issue with the shaders.</br>

#### Update log.. 2/4/2020
Found the issue with the VAO. Buffer over run. I set the size of the buffers too small by one.</br>
Fixed a bug in loading textures.</br>
Added a model and its textures for testing.</br>
Added a FPS counter, render time clock and total triangles rendered count to the text.</br>
Changed the range of the light from 1,000 to 2,000.</br>
I'm still trying to find out why the VBO is loosing its attribute bindings.</br>

#### Update Log... 2/3/2020
Added code to load primitives models.</br>
Added test code to load and render VAO and Display lists.</br>
Using Vertex Array Objects render 9 times slower than Display lists.</br>
Added code to find and load a file not found in the shared or map pkg file.</br>
On start up the app loads the part 2 of the rail station on Himmelsdorf and builds the display list and VAO.

To change to one or the other change the #if 0 to 1 or 0 in the modRender file.</br>
VAO attributes are not saving. I think I have something wrong with its creation but I have no clue.</br>


#### Update log... 1/31/2020
Added a menu item "Load Map" that returns the app to the mouse over map selection screen.</br>
Added the mouse over map selection code. And Text over the icons.</br>
I had to turn the DrawText in to a structure and create multiple references to deal with text rendered on different sized textures.</br>
Added the huge memory mapped virtual file for storage of the terrain mesh while doing normalizion and other work. This might change!</br>
Added a datatable to store the info of already loaded textures. This is easier and much faster than having a bunch of arrays.</br>
I made the stream texture loader much more robust so I don't have to have 3-4 loaders for the texture settings.</br>
Added in Ionic Zip wrapper.</br>
Removed junk out of the render routine. General code clean up.


#### Update log... 1/30/2020
Added code to change the cursor icon when the shift (move X, Z) and ctrl (move Y) are pressed.</br>
Added a cross hair for navigation.</br>
Added game path setting menu item and the code to check it is set correctly at start up and set it if not.</br>
Added code to show a startup message (and the graphic for it) in the frmMain's window.</br>
This is done by placing a panel control over the glControls until the initialization is complete at which point, the panel is disposed.<br>
Added a timer to deal with the frmMain loading so that the startup message is visible while nuTerra initializes its render buffers and all other data needed.</br>
Added in defualt index.html help page and event for its button on the menu.</br>
Added in the 3 Spacebin modules for extracting data from the space.bin map data file. None of it is used yet.</br>
Deferred shading is working with the test objects.</br>

