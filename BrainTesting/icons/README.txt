Window-control icons for the node editor's title bar.

WHERE THEY CAME FROM
    The owner's icon stash at "G:\backup\AMD3500 files\icons", 2,226 16x16
    PNGs. Copied in here rather than read from there at runtime: that is a
    backup drive, and a UI that loses its buttons when a drive is not mounted
    is worse than one that never had them.

    The set is Fugue Icons by Yusuke Kamiyamane, identified from the filenames
    and the artwork. Fugue is published under CC BY 3.0, which asks for
    attribution - so this file is the attribution, and somebody who knows the
    provenance for certain should confirm it.

WHAT EACH ONE WAS
    window-min.png       <- minus.png
    window-max.png       <- application-resize-full.png
    window-restore.png   <- application-resize-actual.png

WHY THEY LOOK MONOCHROME IN THE APP
    They are drawn for light toolbars. Measured against this app's title bar:

        window-min        56 opaque px   mean luminance  72/255
        window-max       240 opaque px   mean luminance 188/255
        window-restore   123 opaque px   mean luminance 162/255

    The two window glyphs read fine on dark; the minus does not - it is a
    smudge. Rather than ship a set where one of three is invisible, BrainIcons
    forces the RGB to white on load and ImGui tints them with the theme's text
    colour, so they match the close box drawn beside them. BrainIcons.MONO =
    False gives the artwork as drawn.

ADDING MORE
    Drop a PNG in here and ask for it by filename without the extension -
    BrainIcons.Tex("window-min"). The whole folder is copied to the build
    output by BrainTesting.vbproj.

Added 2026-09-17 by Tank AI work.
