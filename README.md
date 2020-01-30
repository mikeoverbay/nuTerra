# NuTerra - To boldly code where no one has gone before!

## This is the home of nuTerra where all the work will be done to rewrite Terra using New OpenGL techniques.

### Update log... 1/30/2020
Added code to change the cursor icon when the shift (move x,z) and ctrl (move z) are pressed.
Added game path setting menu item and the code to check it is set correctly at start up and set it if not.</br>
Added code to show a startup message (ant the graphic for it) in the frmMain's window.</br>
This is done by placing a panel control over the glControls until the initialization is complete at which point, the panel is disposed.<br>
Added a timer to deal with the frmMain loading so that the startup message is visible while nuTerra initializes its render buffers and all other data needed.</br>
Added in defualt index.html help page and event for its button on the menu.</br>
Added in the 3 Spacebin modules for extracting data from the space.bin map data file. None of it is used yet.</br>
Deferred shading is working with the test objects.</br>

