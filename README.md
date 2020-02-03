## NuTerra - A work in progress...

### This is the home of nuTerra where all the work will be done to rewrite Terra using New OpenGL techniques.

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

