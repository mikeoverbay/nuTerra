# OWNER.md

The owner's note file. He writes what he wants looked at here, names the session
it is for, and says to clear the file when it is done.

**Empty as of 2026-09-15 - nothing outstanding.**

Cleared by Shader IDE + engine after the tank-shadow bug below was fixed and the
owner confirmed it on screen. The previous contents are in this file's git
history; the fix itself is in the commit that cleared this.

    "bug in tank shadowing. We are writing depth before the tank has moved to
     in current frame. Front of tank shadows when moving backwards.
     Abby AI J 8.5 coords. tank shadow strange."

Cause: advance_movement() runs INSIDE TankRenderer.DrawInner, so baking the
shadow before Draw used the previous frame's position. Fixed by running Draw
first. See modRender.vb and MapTankShadow.Render's summary.
